#include <secrandom_integration/client.hpp>

int main() {
    secrandom::integration::automation_client client;
    secrandom::integration::draw_options options;
    options.count = 2;
    (void)client.pipe();
    (void)options;
    return 0;
}
