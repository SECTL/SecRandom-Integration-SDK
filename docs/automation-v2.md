# SecRandom automation IPC v2

The language bindings in this repository are adapters for the same local v2 protocol. They do
not contain another draw engine. SecRandom remains responsible for the active roster, draw mode,
fairness policy, history, security verification, animations, and result presentation.

Integration requires desktop SecRandom 3.2.0 or later. The independent C# DrawKit does not
require SecRandom. Some corrections, including sparse draw overrides and the event switch, require
a SecRandom build containing the corresponding fixes after 3.2.0.

## Transport

Each request uses a current-user local pipe and one UTF-8 JSON line. The server returns one JSON
line and closes the connection.

| Platform | Request pipe |
| --- | --- |
| Windows | `\\.\pipe\SecRandom_IPC_SecRandom_3F2A1B0E` |
| Linux/macOS | `${TMPDIR:-/tmp}/CoreFxPipe_SecRandom_IPC_SecRandom_3F2A1B0E` |

The protocol version is `2` and the maximum frame is 256 KiB. No TCP listener is involved. The
application setting `AutomationIpcEnabled` and its normal local security checks still apply.

```json
{"version":2,"type":"rpc","id":"42","method":"draw.rollCall.trigger"}
```

```json
{"version":2,"type":"rpc","id":"42","ok":true,"result":{"target":"roll_call","drawn":[]}}
```

An unsuccessful response has `ok:false` and an `error` object containing `code`, `message`, and
optionally `retryable` and `details`.

## Intent-oriented draw API

All four bindings expose the same three channels:

| Channel | v2 method | Main page used by `open_main_window` |
| --- | --- | --- |
| Roll call | `draw.rollCall.trigger` | `main.rollCall` |
| Quick draw | `draw.quick.trigger` | `main.rollCall` |
| Lottery | `draw.lottery.trigger` | `main.lottery` |

The optional fields are `list`, `count`, `gender`, and `group`. Bindings omit fields whose value
was not supplied. With no overrides, the command has no `params` member and SecRandom uses its
current UI settings. The bindings intentionally do not send `random`, `fair`, or a hard-coded
count by default. This keeps new settings and future draw modes owned by SecRandom.

`gender` and `group` apply to roll call. SecRandom rejects them for lottery because lottery uses
prize-pool conditions instead. Quick draw keeps its fixed one-result behavior.

C# 1.2 also accepts `new DrawOptions { Conditions = new LotteryDrawConditions(...) }` for lottery
prize tags and recipient filters. Recipient gender or group requires `StudentList`. Sparse draw
overrides require a SecRandom build containing the IPC draw parameter normalization fix; older
builds may reject them as invalid.

`open_main_window` is also opt-in. When it is true, the adapter sends `window.show` immediately
before the draw. When it is false, it leaves the current windows unchanged; it never hides a
window as a side effect.

Changing the active list is a separate operation:

```json
{"version":2,"type":"rpc","id":"43","method":"roster.activate","params":{"kind":"students","list":"一班"}}
```

For a one-off draw, prefer the draw option `list`; it does not change the default list. Use
`roster.activate` only when the integration intentionally wants to change SecRandom's active
default.

## Reading choices for a host UI

The bindings expose the read-only `roster.lists` and `roster.read` methods. `roster.lists` gives
the available student lists and prize pools. `roster.read` accepts `roster_kind` (`students` or
`prizes`), an optional `list_name`, and `include_disabled`; each returned member contains its
name/id plus `gender`, `group`, and `enabled` values. The bindings also provide a convenience
selection snapshot that flattens the response and returns distinct `groups` and `genders`.

```json
{"version":2,"type":"rpc","id":"44","method":"roster.lists"}
{"version":2,"type":"rpc","id":"45","method":"roster.read","params":{"roster_kind":"students","list_name":"一班","include_disabled":false}}
```

A host can render those arrays in its own UI, let the user choose a list/group/gender, then pass
the chosen values to `draw.rollCall.trigger` or `draw.lottery.trigger`. Reading does not change
SecRandom's selected list. `roster.activate` is the separate, explicit operation for changing
the default list.

## Bindings

- C#: `SecRandom.Integration.Client.AutomationClient` in the existing client package.
- C++: C++17 header-only adapter under `bindings/cpp`.
- Python: dependency-free package under `bindings/python`.
- Rust: synchronous crate under `bindings/rust/secrandom-integration`.

The C++ adapter returns the raw response line so the host can keep its existing JSON library.
The other bindings decode the response envelope and raise/return a typed protocol error. All
bindings expose a generic `call` operation for methods added later without changing the adapter
core.

The C# 1.2 development client also exposes convenience methods for system and state queries,
windows, settings, roster writes, draw configuration and control, results, and history. Settings
writes use `SetSettingsAsync` with a dictionary of schema paths; `WriteRosterAsync` accepts
`RosterKind`, a list name, members, and `RosterWriteMode`. `Replace` overwrites the target roster;
use `Merge` to merge records. Stable state methods return typed results; other server-defined
payloads are returned as `JsonElement`.

```csharp
await client.SetWindowAsync(AutomationWindow.Floating, WindowAction.Show);
await client.WriteRosterAsync(RosterKind.Students, "Class A",
    new object[] { new { id = "1", name = "Alice" } }, RosterWriteMode.Merge);
```

`draw.await` waits for the current draw and returns the latest result. History queries currently
accept `kind` and `name`, not pagination or date filters. Roster reads can truncate members; inspect
each list's `truncated` field.

## Event stream

C# 1.2 development exposes the separate long-lived event pipe as an async stream:

```csharp
var events = new AutomationEvents();
await foreach (var item in events.SubscribeAsync(new[] { "draw.completed" }, cancellationToken))
    Console.WriteLine($"{item.Sequence}: {item.Name} {item.Data}");
```

Disposal or cancellation closes the pipe. The stream does not reconnect automatically, and the
server does not replay missed events. Current events primarily report automation commands, not
every UI operation. SecRandom builds containing the event-gate fix reject and close subscriptions
when automation IPC is disabled; older builds leave the event pipe enabled independently of that
setting. This stream is separate from the signed, user-approved `NotificationReceiver` channel.

## Compatibility

The pipe name and method names are part of the v2 contract. Clients should call
`system.capabilities` when they need to probe an older/newer SecRandom build. A missing method,
disabled IPC, a local verification prompt, or a busy draw is a normal protocol error and should
be surfaced to the caller instead of retried blindly.

For a legacy v1 route without a v2 equivalent, C# 1.2 development offers
`CallLegacyAsync("data/roll_call_list?name=...")`. It returns the complete v1 JSON envelope;
check both `success` and `result.status`, since a business error can have `success: true`.
