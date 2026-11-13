# Messaging POCs: legacy, migrated and modern desktop apps on one broker

Three WPF apps, one per framework, plus the SignalR POC. They all show the API's equipment list and keep it current,
and they all talk to the **real `CleanArch.Api`**. What differs is the event format each one speaks and whose events
it accepts.

| App | Framework | Event format | Library | Receives from | Sends to |
|---|---|---|---|---|---|
| **`Poc.Net472`** | .NET Framework 4.7.2 | legacy (`RemotePubSubEvent<T>`) | `Common.RabbitMQ` + Prism | API, net472, net8 | net472, net8, **API** |
| **`Poc.Net8`** | .NET 8 | legacy (`RemotePubSubEvent<T>`) | `Common.RabbitMQ` + Prism | API, net472, net8 | net472, net8 |
| **`Poc.Net10`** | .NET 10 | modern (`[Message]` classes) | `Messaging.Hosting`, or SignalR | **API**, plus the POC shape events | nobody |
| `Poc3.SignalR` | .NET 8 | modern | SignalR client | API only | nobody |

All of them **change equipment over HTTP** (Add / Delete). The broker carries only events.

---

## Who hears what

```
                    HTTP POST/DELETE /equipment  (any window, Swagger, curl)
                                   │
                            CleanArch.Api ─ commit ──► exchange "CleanArch" ──┬──► net472
                              ▲      │                  Equipment.Created/      ├──► net8
                              │      └── SignalR hub    Updated/Deleted         └──► net10 (RabbitMQ mode)
                              │           └──────────────────────────────────────► net10 (SignalR mode), POC 3
                              │
         EquipmentDisposed    │ (the API deletes the equipment → EquipmentDeleted to everyone)
                              │
  net472 ──► exchange "Poc.Net472" ──┬──► other net472 windows
                                     ├──► net8
                                     └──► API (EquipmentDisposed only)

  net8   ──► exchange "Poc.Net8"   ──┬──► other net8 windows
                                     └──► net472
```

Each app **owns one exchange** and publishes only there; what it receives is decided by the exchanges it binds
(`Subscriptions` in its `appsettings.json`):

| Queue of | Binds | So it gets |
|---|---|---|
| net472 | `CleanArch`, `Poc.Net472` (own), `Poc.Net8` | the API's events, notes and disposals from both legacy apps |
| net8 | `CleanArch`, `Poc.Net8` (own), `Poc.Net472` | the same |
| net10 | `CleanArch`, `Poc.Net472`, `Poc.Net8` | the API's events, and the ten shape events |
| the API (`cleanarch.api.main`, shared, durable) | `Poc.Net472` | `EquipmentDisposed` only: the one event it handles |
| `Poc.Api.NoOutbox` (`poc.api.nooutbox.main`, shared, durable) | `Poc.Net472`, `Poc.Net8` | disposals, flags and the ten shape events |

Every queue binds each exchange **once**, keyed by the exchange's name, which is the routing key its owner publishes
everything with (the real legacy library does the same). So a queue receives everything on the exchanges it binds, and
drops what nothing in the app subscribes to. **net10 gets notes and disposals from the legacy apps too, and drops
them:** it subscribes only to `Equipment.Messages` and `Poc.Shapes` classes. It doesn't reference the legacy events
project.

### The events

| Event | Wire name (`event-type`) | From | Format |
|---|---|---|---|
| Equipment created / updated / deleted | `Equipment.EquipmentCreated` … | the API, after the commit | `Equipment.Messages` classes |
| A note on a piece of equipment | `Poc.Legacy.Events.EquipmentNoteSent` | net472, net8 | `RemotePubSubEvent<EquipmentNote>` |
| Equipment disposed of | `Poc.Legacy.Events.EquipmentDisposed` | net472 | `RemotePubSubEvent<EquipmentDisposal>` |
| Equipment flagged | `Poc.Legacy.Events.EquipmentFlagged` | net472 | `RemotePubSubEvent<EquipmentFlag>` |
| Ten payload shapes (below) | `Poc.Legacy.Events.Shapes.<Name>` | net472, net8 | `RemotePubSubEvent<T>`, read by net10 and `Poc.Api.NoOutbox` as `Poc.Shapes` classes |
| A row was selected (**local only**) | none: never on the wire | net472, net8, inside the app | `PubSubEvent<EquipmentRow?>` |

### The ten shape events

Each one carries a payload shape that can break JSON compatibility between the legacy format (Newtonsoft 12) and the
modern one (System.Text.Json). **Send all 10 shapes** in a net472 or net8 window publishes one of each, the legacy way
(`PublishRemote`); every net472 and net8 window, net10 in RabbitMQ mode, and `Poc.Api.NoOutbox` log what they read.
The same values in every log means both formats agree.

| Event | Shape | Sent |
|---|---|---|
| `StatusChanged` | an enum, as its number | `InRepair (1)` |
| `MaintenanceScheduled` | a UTC `DateTime` and a `DateTimeOffset` | now + 14 days, and now |
| `CostRecorded` | decimals, one whole | `1299.95`, `12` (the legacy format writes `12.0`) |
| `WarrantyChecked` | nullable values, some null | `null`, `0`, `null` |
| `TagsChanged` | a list of strings | `"laptop"`, `""`, `"Büro <3>"` |
| `LocationMoved` | nested objects | HQ floor 2 → Annex floor -1 |
| `AttributesChanged` | a dictionary; keys keep their case | `RAM`, `cpu`, `Screen.Size` (value with a `"`) |
| `UsageReported` | `long`, `double`, `bool` | 5 000 000 000, 71.25, true |
| `BatchCounted` | a list of objects | Laptop 12, Monitor 0 |
| `AuditRequested` | no properties: the body is `{}` | — |

One class per event serves both sides, as the API's `Equipment.Messages` does: `Poc.Shapes` holds the payloads, each
with `[Message("Poc.Legacy.Events.Shapes.<Name>")]` for the modern apps, and `Poc.Legacy.Events/Shapes` wraps each in a
`RemotePubSubEvent<T>` whose full name is that wire name. They're POC-only: nothing outside `samples/` uses them.

Checked on 2026-11-05 against RabbitMQ 4.3.6. Sent from net472: net8, net10 (RabbitMQ) and `Poc.Api.NoOutbox` each
logged the same ten values, `12.0` included. Sent from net8: `Poc.Api.NoOutbox` logged all ten again. net10 in SignalR
mode received none, as expected. Checked again after the switch to exchange-name routing keys (one binding per
exchange): sent from net472, net8, net10 and `Poc.Api.NoOutbox` each received all ten, and a laptop created over HTTP
reached all three windows.

**Local-only events.** `EquipmentSelected` (in each legacy app's own `Events/` folder) is raised with plain
`Publish()` and heard by the status bar's view model in the same app. What keeps it off the broker is its base class:
it's a plain `PubSubEvent<T>`, so it gets no binding, is never received, and `PublishRemote` on it throws.

**How one wire carries both formats.** A legacy app knows an event by its class's **full name**; a modern app or the
API by its `[Message("…")]` name. The body is JSON either way. So:

- The legacy apps receive the API's events through `Poc.Legacy.Events/FromApi/`: classes in namespace `Equipment`,
  named after the API's events (`Equipment.EquipmentCreated : RemotePubSubEvent<Equipment.Messages.EquipmentCreated>`). The
  payload is the API's own class: System.Text.Json writes it, Newtonsoft reads it.
- The API receives `EquipmentDisposed` through its own copy,
  `Equipment.Infrastructure.IntegrationEvents.EquipmentDisposed`, marked
  `[Message("Poc.Legacy.Events.EquipmentDisposed")]` with the same properties. Its handler sends the same
  `DeleteEquipment` command as `DELETE /equipment/{id}`, so the delete is validated, transactional, audited (actor
  "system") and announced exactly like an HTTP one. A repeat delivery is harmless.

### Why net8 is the net472 app, recompiled

`Poc.Net8` is `Poc.Net472` with a different exchange and no disposal button. That's the point: a legacy app moved to
.NET 8 keeps its Prism events and the legacy adapter (which targets both frameworks) and goes on talking to the
.NET Framework apps exactly as before. Moving it to the modern format is a separate, later step; `Poc.Net10` shows
where that ends.

### net10: two transports, one app

`Events:Transport` in `Poc.Net10/appsettings.json` picks how the API's events arrive. The view model, the list and the
event classes are the same either way.

| | `RabbitMQ` (default) | `SignalR` |
|---|---|---|
| Delay after a change | immediate | immediate |
| Survives an API crash mid-change | ❌ best effort | ❌ best effort |
| Needs on the desktop | a broker account + the API key | the API key only |
| Network | broker (5672) and the API | the API only |

Override per window: `Poc.Net10.exe --Events:Transport=SignalR`. For a new app whose events all come from the API,
SignalR is the default recommendation ([POC 3](#poc-3-signalr), fewer moving parts).

---

## Running them

1. **RabbitMQ** on `localhost:5672` with the management plugin — see
   [Running it on your machine](../../docs/messaging/README.md#running-it-on-your-machine).
2. **Create the exchanges** once per broker, so the apps can start in any order:
   ```powershell
   .\samples\MessagingPocs\setup-exchanges.ps1
   ```
   Without it, an app waits (and says so) until every exchange it binds exists: net472 waits for net8 to have run
   once and the other way round, and the API's consumer waits for net472. The API publishes regardless.
3. **The API** in Development (its `appsettings.Development.json` has the `Messaging` section, including the
   subscription to `Poc.Net472`):
   ```powershell
   dotnet run --project src/Api/CleanArch.Api
   ```
4. **Build:** `dotnet build samples/MessagingPocs/MessagingPocs.slnx`
5. **Open windows** (any number of each):
   ```powershell
   samples\MessagingPocs\Poc.Net472\bin\Debug\net472\Poc.Net472.exe
   samples\MessagingPocs\Poc.Net8\bin\Debug\net8.0-windows\Poc.Net8.exe
   samples\MessagingPocs\Poc.Net10\bin\Debug\net10.0-windows\Poc.Net10.exe
   samples\MessagingPocs\Poc.Net10\bin\Debug\net10.0-windows\Poc.Net10.exe --Events:Transport=SignalR
   samples\MessagingPocs\Poc3.SignalR\bin\Debug\net8.0-windows\Poc3.SignalR.exe
   ```

**Things to try**

- **Add a laptop in any window.** It appears everywhere, from the API's event: SignalR and RabbitMQ windows
  alike, right after the API commits.
- **Send a note from net472 or net8.** Every net472 and net8 window logs it. The net10 windows log nothing.
- **Send all 10 shapes from net472 or net8.** Every legacy window, net10 (RabbitMQ mode) and `Poc.Api.NoOutbox`
  log the same ten values.
- **Dispose of a laptop from net472.** The net8 window logs the disposal; the API logs
  `Equipment … disposed by net472 …; removed from inventory: True`; then the row disappears from **every**
  window, net10 included — through the API's `EquipmentDeleted`, not the disposal.
- **Select a row in net472 or net8.** The status bar says so (a local event); no other window reacts.
- **Watch the management UI** (`http://localhost:15672`) → Exchanges → `Poc.Net472` → Bindings: the net8 queues, the
  other net472 queues, the net10 queues and the two APIs' queues, each bound once with the key `Poc.Net472`.
- **Stop the API, dispose of something, start the API.** The disposal waits in the API's durable queue and is handled
  when it comes back. (Desktop queues are per window and vanish with it; the API's doesn't.)

---

## The pitfalls, in one list

Each is explained where it happens in the code.

**All apps**

- ❌ Changing the API's data any way but HTTP. The API validates, saves, audits, THEN tells everyone.
  (`EquipmentDisposed` is an existing legacy event the API was taught to handle — through the same command.)
- ❌ Assuming the app will be told about everything. Load on start-up and after every reconnect; events only keep
  the list current. Subscribe/connect first, then load.
- ❌ Updating bound collections from the thread an event arrives on. Move to the UI thread first.
- ❌ Applying events in a way that breaks if one arrives twice, or during a load (`Poc.Shared/EquipmentList.cs`).
- ❌ Adding or removing a row locally after your own call succeeds. The API's event does it; doing both shows it twice.
- ❌ A new `HttpClient` per call. Keep one with a pooled-connection lifetime (`Poc.Shared/EquipmentApi.cs`; on
  .NET Framework, a ServicePoint connection lease).
- ❌ Blocking on async shutdown on the UI thread. Hand it to the thread pool, or the app hangs on exit.

**Legacy format (net472, net8)**

- ❌ Renaming or moving an event class. Its full name is the wire name: every app listening for the old one, the API
  included, silently stops receiving it.
- ❌ Prism subscriptions with capturing lambdas. Weak references: they're collected and stop silently.
- ❌ Deriving a local-only event from `RemotePubSubEvent<T>`. It isn't sent unless you call `PublishRemote`, but it
  says the wrong thing to the next reader. Use `PubSubEvent<T>`, which stays inside the app.
- ❌ Using the legacy adapter in a NEW app. It exists so existing apps keep working unchanged.

**Modern format (net10)**

- ❌ Expecting a message that arrived while nothing was subscribed. It is dropped on arrival: subscribing doesn't
  replay anything.
- ❌ Forgetting to dispose `IMessageSubscriber` subscriptions. Strong references: closed windows leak.
- ❌ Adding a legacy app's exchange to `Subscriptions` without routing keys: every event the app receives would bind
  there, and its other events would follow.
- ❌ Publishing. No `IMessagePublisher`: modern apps only listen.

**SignalR (net10 in SignalR mode, POC 3)**

- ❌ `WithAutomaticReconnect()` with no arguments: it gives up after ~45 s. Use a policy that keeps trying
  (`Poc.Shared/SignalR/ForeverRetryPolicy.cs`).
- ❌ Expecting automatic reconnect to cover the first connection, or forgetting that a reconnect loses group
  membership (`Poc.Shared/SignalR/EquipmentLiveUpdates.cs`).
- ❌ Retrying a rejected credential (401) forever.

---

## POC 3: SignalR

Unchanged: a .NET 8 app that listens only to the API's hub, with presence. The SignalR connection code now lives in
`Poc.Shared/SignalR/` so the net10 app uses the same. What the server still needs before it's production-ready:

| Gap | Today | Needed |
|---|---|---|
| Browser clients | The hub needs a header a browser can't send on a WebSocket | An `access_token` hook for Okta tokens on `/hubs` paths ([tutorial 65 §9](../../tutorials/65-real-time-notifications.md#9-step-6--a-client-minimally)) |
| IIS | — | Enable the **WebSocket Protocol** Windows feature on the server |
| Scale-out | One server (fine for now) | Redis backplane before a second API instance ([tutorial 65 §12](../../tutorials/65-real-time-notifications.md#12-scaling-past-one-instance)) |

---

## An API without the outbox

`Poc.Api.NoOutbox` is a small API that publishes straight to RabbitMQ and subscribes to events. It has no database,
and it needed no changes to the messaging libraries: it publishes through `IMessagePublisher` and receives through
`IMessageSubscriber`, both registered by `AddMessaging`.

| Endpoint | Publisher | Responds when | Broker down | Process crashes |
|---|---|---|---|---|
| `POST /equipment/buffered` | `IMessagePublisher` | the message is in memory: `202` | `202`, sent after the bus reconnects | whatever is still in memory is lost |

```powershell
dotnet run --project samples\MessagingPocs\Poc.Api.NoOutbox
curl.exe -H "Content-Type: application/json" -d '{\"name\":\"Laptop\",\"category\":\"IT\",\"assetTag\":\"A-1\"}' http://localhost:5240/equipment/buffered
```

It publishes the API's `EquipmentCreated` on the `CleanArch` exchange, so the same bytes reach the same receivers and
they don't change. These events don't come from the real API, so a window shows the new row only until it reloads its
list from the API.

**Subscribing.** `EquipmentDisposedHandler` receives `EquipmentDisposed` from the net472 app's exchange and logs it,
wired the way `CleanArch.Api` does it: subscribed in `Program.cs` with a DI scope per message, and `Subscriptions` plus
`"QueueMode": "Shared"` in `appsettings.json`. Its queue, `poc.api.nooutbox.main`, is durable and has a dead-letter
queue, so messages wait while the API is stopped and a subscriber that keeps failing doesn't lose them. Run `setup-exchanges.ps1` first if the
`Poc.Net472` exchange doesn't exist yet. Click **Dispose of selected** in a net472 window to see it. The real API
also handles this event, and deletes that equipment if it's running.

**Subscribing without a class.** `EquipmentFlagged : RemotePubSubEvent<EquipmentFlag>` is sent by **Flag selected**
in the net472 window, as `{"Id":"...","Name":"..."}`. Its callback is inline in `Program.cs`, with no scope, for small
jobs: a message that arrives before `Subscribe` is called is dropped, so subscribe before `app.Run()`. The real API doesn't handle this event.

**A routing-key pattern.** The flag subscription binds `equipment.#`, a pattern, while the disposal subscription binds
the exact key `equipment`. Both match what the net472 window sends (`#` matches zero or more words), and the flag
would still arrive if that app moved to keys such as `equipment.flagged`. The log line shows the key it came with.

**No tuple payloads.** This event's payload was a tuple until 2026-11-09. A tuple's items are fields, which
System.Text.Json (the one serializer, legacy apps included) doesn't serialize, so they arrived empty. Tuples are now
rejected when the event is first used: use a class with `{ get; set; }` properties, or a primitive.

**What you give up:** a change and its event can't be saved in one transaction. A database save can succeed and the
API then stop before the event is sent, and nothing sends it later. `CleanArch.Api` accepts the same trade-off: it
publishes after the commit, with no outbox.

Checked against a local RabbitMQ 4.3.6, with a temporary queue bound to `CleanArch` rather than the POC windows. With
the broker up, `buffered` returned `202` and the message arrived. With it stopped, `buffered` still returned `202`,
and after a restart the buffered message arrived. For subscribing, a message published on `Poc.Net472` through the management API, with `event-type` set, reached the
handler. (That check used the wire name as routing key; since 2026-11-05 the key is the exchange name, checked under *The
ten shape events*.) The net472 window itself wasn't used. For the tuple, `PublishRemote` through the real adapter on net472, from a small console app rather than the window, reached this API and a legacy listener on net8 with both values intact.

## Layout

```
Poc.Legacy.Events/   the legacy apps' RemotePubSubEvent<T> events; FromApi/ = the API's events under their wire names;
                     Shapes/ = the ten shape events, and ShapeEvents (send all) / ShapeEventsLog (receive all)
Poc.Shapes/          the ten shape payloads as [Message] classes, read by both formats. netstandard2.0
Poc.Shared/          EquipmentApi (HTTP), EquipmentList (load + apply), ApiOptions; SignalR/ (net8+ only). net472 + net8.0
Poc.Net472/          legacy app: App.xaml.cs (RabbitMQServiceRouter.FromConfiguration), ViewModels/ (Subscribe, PublishRemote)
Poc.Net8/            the same app on .NET 8
Poc.Net10/           modern app: App.xaml.cs (Generic Host; RabbitMQ or SignalR), ViewModels/
Poc3.SignalR/        SignalR only, with presence
Poc.Api.NoOutbox/    an API that publishes straight to RabbitMQ, with no database, and subscribes
setup-exchanges.ps1  creates the four exchanges
```

On the API side: `src/Modules/Equipment/Equipment.Infrastructure/IntegrationEvents/EquipmentDisposed*.cs` and
`EquipmentSubscriptions.cs`, and `Subscriptions` in `src/Api/CleanArch.Api/appsettings.Development.json`.

### One solution per app

`MessagingPocs.slnx` opens everything. Each app also has its own solution, holding only the projects it builds with, in
folders: **App**, **Messaging** (the RabbitMQ library projects), **Events** (the message and event classes) and
**Shared** (the POCs' own helpers). Which ones the app references itself, and which come along with those:

| Solution | Referenced by the app | Come along |
|---|---|---|
| `Poc.Net472.slnx` (legacy, Prism) | `Common.RabbitMQ`, `Poc.Legacy.Events`, `Poc.Shared` | `Messaging.RabbitMQ`, `Messaging.Abstractions`, `Equipment.Messages`, `Poc.Shapes` |
| `Poc.Net8.slnx` (legacy, Prism, on .NET 8) | the same as net472 | the same as net472 |
| `Poc.Net10.slnx` (modern, no Prism) | `Messaging.Hosting`, `Poc.Shapes`, `Poc.Shared` | `Messaging.RabbitMQ`, `Messaging.Abstractions`, `Equipment.Messages` |
| `Poc3.SignalR.slnx` (SignalR only) | `Poc.Shared` | `Equipment.Messages`, `Messaging.Abstractions` (for `[Message]` on the contracts) |
| `Poc.Api.NoOutbox.slnx` (API) | `Messaging.Hosting`, `Equipment.Messages`, `Poc.Shapes` | `Messaging.RabbitMQ`, `Messaging.Abstractions` |

Only the messaging projects (`Common.RabbitMQ` for a legacy app, `Messaging.Hosting` for a modern one or an API) are
needed in an app of your own. `Poc.Shared`, `Poc.Legacy.Events` and `Poc.Shapes` belong to these POCs, and
`Equipment.Messages` is there because the POCs show the API's events.

## Clean-up

The broker keeps the exchanges and the API's durable queues (`cleanarch.api.main`, `.dead-letter`) after everything
closes; they're harmless. Delete them in the management UI for a clean broker.

## How these were checked

Built with no warnings. Run against a local RabbitMQ 4.3.6 and the API in Development, one window of each new app:
the broker's bindings matched the tables above (net10 bound to `CleanArch` only; the API bound to `Poc.Net472` for
`EquipmentDisposed` only). A laptop created over HTTP was delivered to all three queues. An `EquipmentDisposed`
published on `Poc.Net472` in the legacy adapter's format was handled by the API, which deleted the laptop (`GET` 404
afterwards); the disposal reached the net472 and net8 queues but not net10's, and the API's `EquipmentDeleted` reached
all three. net10 in SignalR mode held one connection, to the API, and none to the broker. The window contents
themselves (button clicks, log lines) were not driven in this check.
