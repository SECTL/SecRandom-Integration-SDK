# SecRandom Integration C++ adapter

This is a C++17, header-only transport adapter. It returns the raw JSON response so the host
application can keep using its existing JSON library. The adapter does not implement draw
selection or force a random/fair mode.

```cpp
#include <secrandom_integration/client.hpp>

secrandom::integration::automation_client client;
secrandom::integration::draw_options options;
options.open_main_window = true;
auto response = client.draw(secrandom::integration::draw_channel::roll_call, options);
```

`client.roster_lists()` returns available list/pool names and `client.read_roster("students")`
returns member JSON containing `gender` and `group`. Render those values in the host UI and pass
the selected list/group to `draw_options`.

On Windows the adapter opens `\\.\pipe\SecRandom_IPC_SecRandom_3F2A1B0E`. On Linux/macOS it
uses the .NET Unix-domain-socket mapping `${TMPDIR:-/tmp}/CoreFxPipe_SecRandom_IPC_SecRandom_3F2A1B0E`.
