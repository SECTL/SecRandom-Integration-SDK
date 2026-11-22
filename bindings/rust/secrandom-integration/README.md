# secrandom-integration

Synchronous Rust adapter for the SecRandom automation IPC v2 protocol. It only serializes
commands and transports them over the current-user pipe; drawing policy remains in SecRandom.

```rust
use secrandom_integration::{AutomationClient, DrawChannel, DrawOptions};

let client = AutomationClient::default();
let result = client.draw(DrawChannel::RollCall, DrawOptions {
    open_main_window: true,
    ..Default::default()
})?;
```

Use `roster_lists`, `read_roster`, or `roster_selection_options` to populate a host application's
list, group, and gender selectors. These calls are read-only.
