#pragma once

#include <chrono>
#include <cstdlib>
#include <cstddef>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>
#endif

namespace secrandom::integration {

inline constexpr int protocol_version = 2;
inline constexpr std::size_t max_frame_bytes = 256 * 1024;
inline constexpr const char* pipe_name = "SecRandom_IPC_SecRandom_3F2A1B0E";
inline constexpr const char* event_pipe_name = "SecRandom_IPC_Events_SecRandom_3F2A1B0E";

enum class draw_channel { roll_call, quick, lottery };

struct draw_options {
    std::optional<std::string> list;
    std::optional<int> count;
    std::optional<std::string> gender;
    std::optional<std::string> group;
    bool open_main_window = false;
};

// The adapter intentionally returns the raw JSON response so applications may use the JSON
// library they already depend on. No draw or roster policy is duplicated here.
class automation_client {
public:
    explicit automation_client(std::string name = pipe_name) : pipe_name_(std::move(name)) {}

    const std::string& pipe() const noexcept { return pipe_name_; }

    std::string call(const std::string& method, const std::optional<std::string>& params = std::nullopt) const {
        if (method.empty())
            throw std::invalid_argument("automation method is required");

        std::ostringstream request;
        request << "{\"version\":2,\"type\":\"rpc\",\"id\":\""
                << request_id() << "\",\"method\":" << quote(method);
        if (params.has_value())
            request << ",\"params\":" << *params;
        request << "}\n";
        const auto request_line = request.str();
        if (request_line.size() > max_frame_bytes)
            throw std::length_error("automation request exceeds the protocol frame limit");

        pipe_connection connection = pipe_connection::connect(pipe_path());
        connection.write(request_line);
        auto response = connection.read_line();
        if (response.size() > max_frame_bytes)
            throw std::length_error("automation response exceeds the protocol frame limit");
        return response;
    }

    std::string draw(draw_channel channel, const draw_options& options = {}) const {
        if (options.open_main_window) {
            call("window.show", std::string("{\"target\":\"main\",\"page\":") +
                 quote(channel == draw_channel::lottery ? "main.lottery" : "main.rollCall") + "}");
        }
        return call(method(channel), draw_params(options));
    }

    std::string activate_roster(const std::string& kind, const std::string& list) const {
        if ((kind != "students" && kind != "prizes") || list.empty())
            throw std::invalid_argument("kind must be students or prizes and list is required");
        return call("roster.activate", std::string("{\"kind\":") + quote(kind) +
                    ",\"list\":" + quote(list) + "}");
    }

    // These methods return raw JSON so a host can render the members with its existing JSON
    // library and let its own UI choose a list, group, or gender.
    std::string roster_lists() const {
        return call("roster.lists");
    }

    std::string read_roster(const std::string& kind,
                            const std::optional<std::string>& list = std::nullopt,
                            bool include_disabled = false) const {
        if (kind != "students" && kind != "prizes")
            throw std::invalid_argument("kind must be students or prizes");
        std::string params = "{\"roster_kind\":" + quote(kind) +
                             ",\"include_disabled\":" +
                             (include_disabled ? "true" : "false");
        if (list.has_value())
            params += ",\"list_name\":" + quote(*list);
        params += "}";
        return call("roster.read", params);
    }

    std::string show_main_window(const std::optional<std::string>& page = std::nullopt) const {
        std::string params = "{\"target\":\"main\"";
        if (page.has_value())
            params += ",\"page\":" + quote(*page);
        params += "}";
        return call("window.show", params);
    }

private:
    class pipe_connection {
    public:
        static pipe_connection connect(const std::string& path) {
#ifdef _WIN32
            const auto handle = CreateFileA(
                path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE)
                throw std::runtime_error("unable to connect to SecRandom automation pipe");
            return pipe_connection(handle);
#else
            const auto descriptor = ::socket(AF_UNIX, SOCK_STREAM, 0);
            if (descriptor < 0)
                throw std::runtime_error("unable to create SecRandom automation socket");
            sockaddr_un address{};
            if (path.size() >= sizeof(address.sun_path)) {
                ::close(descriptor);
                throw std::length_error("Unix socket path is too long");
            }
            address.sun_family = AF_UNIX;
            path.copy(address.sun_path, path.size());
            address.sun_path[path.size()] = '\0';
            if (::connect(descriptor, reinterpret_cast<const sockaddr*>(&address), sizeof(address)) < 0) {
                ::close(descriptor);
                throw std::runtime_error("unable to connect to SecRandom automation socket");
            }
            return pipe_connection(descriptor);
#endif
        }

        pipe_connection(const pipe_connection&) = delete;
        pipe_connection& operator=(const pipe_connection&) = delete;

        pipe_connection(pipe_connection&& other) noexcept
#ifdef _WIN32
            : handle_(std::exchange(other.handle_, INVALID_HANDLE_VALUE)) {}
#else
            : descriptor_(std::exchange(other.descriptor_, -1)) {}
#endif

        ~pipe_connection() {
#ifdef _WIN32
            if (handle_ != INVALID_HANDLE_VALUE)
                CloseHandle(handle_);
#else
            if (descriptor_ >= 0)
                ::close(descriptor_);
#endif
        }

        void write(std::string_view value) {
#ifdef _WIN32
            std::size_t offset = 0;
            while (offset < value.size()) {
                DWORD written = 0;
                const auto remaining = value.size() - offset;
                if (!WriteFile(handle_, value.data() + offset,
                               static_cast<DWORD>(remaining), &written, nullptr) || written == 0)
                    throw std::runtime_error("failed to write SecRandom automation request");
                offset += written;
            }
#else
            std::size_t offset = 0;
            while (offset < value.size()) {
                const auto written = ::send(descriptor_, value.data() + offset, value.size() - offset, 0);
                if (written <= 0)
                    throw std::runtime_error("failed to write SecRandom automation request");
                offset += static_cast<std::size_t>(written);
            }
#endif
        }

        std::string read_line() {
            std::string result;
            result.reserve(256);
            while (result.size() <= max_frame_bytes) {
                char character = '\0';
#ifdef _WIN32
                DWORD read = 0;
                if (!ReadFile(handle_, &character, 1, &read, nullptr) || read == 0)
                    throw std::runtime_error("SecRandom closed the automation pipe");
#else
                const auto read = ::recv(descriptor_, &character, 1, 0);
                if (read <= 0)
                    throw std::runtime_error("SecRandom closed the automation socket");
#endif
                if (character == '\n') {
                    if (!result.empty() && result.back() == '\r')
                        result.pop_back();
                    return result;
                }
                result.push_back(character);
            }
            throw std::length_error("automation response exceeds the protocol frame limit");
        }

    private:
#ifdef _WIN32
        explicit pipe_connection(HANDLE handle) : handle_(handle) {}
        HANDLE handle_ = INVALID_HANDLE_VALUE;
#else
        explicit pipe_connection(int descriptor) : descriptor_(descriptor) {}
        int descriptor_ = -1;
#endif
    };

    std::string pipe_name_;

    std::string pipe_path() const {
#ifdef _WIN32
        return std::string("\\\\.\\pipe\\") + pipe_name_;
#else
        const auto* temp_dir = std::getenv("TMPDIR");
        return std::string(temp_dir && *temp_dir ? temp_dir : "/tmp") +
               "/CoreFxPipe_" + pipe_name_;
#endif
    }

    static const char* method(draw_channel channel) {
        switch (channel) {
        case draw_channel::roll_call: return "draw.rollCall.trigger";
        case draw_channel::quick: return "draw.quick.trigger";
        case draw_channel::lottery: return "draw.lottery.trigger";
        }
        throw std::invalid_argument("unknown draw channel");
    }

    static std::optional<std::string> draw_params(const draw_options& options) {
        std::string params = "{";
        bool has_value = false;
        auto add_string = [&](const char* key, const std::optional<std::string>& value) {
            if (!value.has_value()) return;
            if (has_value) params += ",";
            params += quote(key) + ":" + quote(*value);
            has_value = true;
        };
        add_string("list", options.list);
        if (options.count.has_value()) {
            if (has_value) params += ",";
            params += "\"count\":" + std::to_string(*options.count);
            has_value = true;
        }
        add_string("gender", options.gender);
        add_string("group", options.group);
        params += "}";
        return has_value ? std::optional<std::string>(params) : std::nullopt;
    }

    static std::string quote(const std::string& value) {
        std::string result = "\"";
        for (unsigned char character : value) {
            switch (character) {
            case '\\': result += "\\\\"; break;
            case '"': result += "\\\""; break;
            case '\b': result += "\\b"; break;
            case '\f': result += "\\f"; break;
            case '\n': result += "\\n"; break;
            case '\r': result += "\\r"; break;
            case '\t': result += "\\t"; break;
            default:
                if (character < 0x20) {
                    constexpr char hex[] = "0123456789abcdef";
                    result += "\\u00";
                    result += hex[(character >> 4) & 0x0f];
                    result += hex[character & 0x0f];
                } else {
                    result += static_cast<char>(character);
                }
                break;
            }
        }
        result += "\"";
        return result;
    }

    static std::string request_id() {
        const auto now = std::chrono::high_resolution_clock::now().time_since_epoch().count();
        return std::to_string(now);
    }
};

} // namespace secrandom::integration
