using System.Text.Json;
using SecRandom.Integration;

namespace SecRandom.Integration.Client;

public enum AutomationWindow { Main, Settings, Floating }
public enum WindowAction { Show, Hide, Toggle }
public enum RosterWriteMode { Replace, Merge }

public sealed record PingResult(bool Pong, DateTimeOffset ServerTime);
public sealed record SystemInfo(string Version, string Protocol, string Platform, string Product);
public sealed record WindowsState(bool Main, bool Settings, bool Floating, bool SimpleMode);
public sealed record RuntimeState(long UptimeSeconds, bool SimpleMode, bool LotteryEnabled, bool Drawing);

public sealed partial class AutomationClient
{
    private async Task<JsonElement> ResultAsync(string method, object? parameters, CancellationToken token)
        => (await CallAsync(method, parameters, token).ConfigureAwait(false)).Result ?? default;

    public async Task<T> CallForAsync<T>(string method, object? parameters = null, CancellationToken token = default)
    {
        var result = await ResultAsync(method, parameters, token).ConfigureAwait(false);
        return result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? throw new InvalidDataException("The automation response has no result.")
            : result.Deserialize<T>(AutomationJson.Options) ?? throw new InvalidDataException("The automation result was invalid.");
    }

    public Task<PingResult> PingAsync(CancellationToken token = default) => CallForAsync<PingResult>("system.ping", token: token);
    public Task<SystemInfo> GetInfoAsync(CancellationToken token = default) => CallForAsync<SystemInfo>("system.info", token: token);
    public Task<JsonElement> GetCapabilitiesAsync(CancellationToken token = default) => ResultAsync("system.capabilities", null, token);
    public Task<JsonElement> GetIntegrationStatusAsync(CancellationToken token = default) => ResultAsync("system.integration.status", null, token);
    public Task<WindowsState> GetWindowsStateAsync(CancellationToken token = default) => CallForAsync<WindowsState>("state.windows", token: token);
    public Task<JsonElement> GetDrawStateAsync(CancellationToken token = default) => ResultAsync("state.draw", null, token);
    public Task<RuntimeState> GetRuntimeStateAsync(CancellationToken token = default) => CallForAsync<RuntimeState>("state.runtime", token: token);

    public Task<JsonElement> SetWindowAsync(AutomationWindow window, WindowAction action, string? page = null, CancellationToken token = default)
        => ResultAsync(action switch { WindowAction.Show => "window.show", WindowAction.Hide => "window.hide", WindowAction.Toggle => "window.toggle", _ => throw new ArgumentOutOfRangeException(nameof(action)) },
            new { target = WindowTarget(window), page }, token);

    public Task<JsonElement> NavigateAsync(AutomationWindow window, string page, CancellationToken token = default)
        => ResultAsync("window.navigate", new { target = WindowTarget(window), page }, token);

    public Task<JsonElement> GetSettingsSchemaAsync(CancellationToken token = default) => ResultAsync("settings.schema", null, token);
    public Task<JsonElement> GetSettingsAsync(CancellationToken token = default) => ResultAsync("settings.get", null, token);
    public Task<JsonElement> SetSettingsAsync(IReadOnlyDictionary<string, object?> patch, CancellationToken token = default)
        => ResultAsync("settings.set", new { patch }, token);
    public Task<JsonElement> OpenSettingsPageAsync(string page, CancellationToken token = default)
        => ResultAsync("settings.page.open", new { page }, token);

    // Members may include optional fields such as tags and specific voice; the host validates them.
    public Task<JsonElement> WriteRosterAsync(RosterKind kind, string list, IEnumerable<object> members,
        RosterWriteMode mode = RosterWriteMode.Replace, bool activate = false, CancellationToken token = default)
    {
        var name = kind == RosterKind.Prizes ? "prizes" : "students";
        var parameters = new Dictionary<string, object?>
        {
            ["roster_kind"] = name, ["list_name"] = list,
            ["mode"] = mode switch { RosterWriteMode.Replace => "replace", RosterWriteMode.Merge => "merge", _ => throw new ArgumentOutOfRangeException(nameof(mode)) },
            ["activate"] = activate, [name] = members
        };
        return ResultAsync("roster.write", parameters, token);
    }

    public Task<JsonElement> ConfigureDrawAsync(DrawChannel channel, DrawOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Conditions is not null || options.OpenMainWindow)
            throw new ArgumentException("Conditions and OpenMainWindow are only supported by DrawAsync.", nameof(options));
        return ResultAsync(DrawMethod(channel, "configure"), options.ToParameters(), token);
    }
    public Task<JsonElement> StartDrawAsync(DrawChannel channel, CancellationToken token = default) => ResultAsync(DrawMethod(channel, "start"), null, token);
    public Task<JsonElement> StopDrawAsync(DrawChannel channel, CancellationToken token = default) => ResultAsync(DrawMethod(channel, "stop"), null, token);
    public Task<JsonElement> ResetDrawAsync(DrawChannel channel, CancellationToken token = default) => ResultAsync(DrawMethod(channel, "reset"), null, token);
    public Task<JsonElement> GetDrawResultAsync(DrawChannel channel, CancellationToken token = default)
        => ResultAsync("draw.result", new { target = DrawTarget(channel) }, token);
    public Task<JsonElement> AwaitDrawAsync(DrawChannel channel, int? timeoutMilliseconds = null, CancellationToken token = default)
        => ResultAsync("draw.await", new { target = DrawTarget(channel), timeout_ms = timeoutMilliseconds }, token);

    public Task<JsonElement> GetHistoryAsync(RosterKind kind, string name, CancellationToken token = default)
        => ResultAsync("history.query", new { kind = HistoryKind(kind), name }, token);
    public Task<JsonElement> GetHistorySummaryAsync(RosterKind kind, string name, CancellationToken token = default)
        => ResultAsync("history.summary", new { kind = HistoryKind(kind), name }, token);
    public Task<JsonElement> GetEventStatusAsync(CancellationToken token = default) => ResultAsync("events.status", null, token);

    private static string WindowTarget(AutomationWindow window) => window switch
    {
        AutomationWindow.Main => "main", AutomationWindow.Settings => "settings", AutomationWindow.Floating => "float",
        _ => throw new ArgumentOutOfRangeException(nameof(window))
    };
    private static string DrawTarget(DrawChannel channel) => channel switch
    {
        DrawChannel.RollCall => "roll_call", DrawChannel.Quick => "quick", DrawChannel.Lottery => "lottery",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };
    private static string DrawMethod(DrawChannel channel, string action) => channel switch
    {
        DrawChannel.RollCall => $"draw.rollCall.{action}", DrawChannel.Lottery => $"draw.lottery.{action}",
        _ => throw new ArgumentException("Quick draw supports trigger only.", nameof(channel))
    };
    private static string HistoryKind(RosterKind kind) => kind switch
    {
        RosterKind.Students => "students", RosterKind.Prizes => "prizes", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
