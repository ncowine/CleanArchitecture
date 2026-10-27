# Messaging POCs: legacy, migrated and modern desktop apps on one broker

Three WPF apps, one per framework, plus the SignalR POC. They all show the API's equipment list and keep it current,
and they all talk to the **real `CleanArch.Api`**. What differs is the event format each one speaks and whose events
it accepts.

| App | Framework | Event format | Library | Receives from | Sends to |
|---|---|---|---|---|---|
| **`Poc.Net472`** | .NET Framework 4.7.2 | legacy (`PubSubEvent<T>`) | `Common.RabbitMQ` + Prism | API, net472, net8 | net472, net8, **API** |
| **`Poc.Net8`** | .NET 8 | legacy (`PubSubEvent<T>`) | `Common.RabbitMQ` + Prism | API, net472, net8 | net472, net8 |
| **`Poc.Net10`** | .NET 10 | modern (`[Message]` classes) | `Messaging.Hosting`, or SignalR | **API only** | nobody |
| `Poc3.SignalR` | .NET 8 | modern | SignalR client | API only | nobody |

All of them **change equipment over HTTP** (Add / Delete). The broker carries only events.

---

## Who hears what

```
                    HTTP POST/DELETE /equipment  (any window, Swagger, curl)
                                   │
                            CleanArch.Api ── outbox ──► exchange "CleanArch" ──┬──► net472
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
| net10 | `CleanArch` only | the API's events, nothing else |
| the API (`cleanarch.api.main`, shared, durable) | `Poc.Net472` | `EquipmentDisposed` only: the one event it handles |

**"API only" for net10 is enforced twice, both by configuration:** the broker never routes a legacy app's message to
its queue (it doesn't bind their exchanges), and it registers only the API's `Equipment.Messages` classes, so it
couldn't read one anyway. It doesn't even reference the legacy events project.

### The events

| Event | Wire name (`event-type`) | From | Format |
|---|---|---|---|
| Equipment created / updated / deleted | `Equipment.EquipmentCreated` … | the API, through its outbox (~2 s) | `Equipment.Messages` classes |
| A note on a piece of equipment | `Poc.Legacy.Events.EquipmentNoteSent` | net472, net8 | `PubSubEvent<EquipmentNote>` |
| Equipment disposed of | `Poc.Legacy.Events.EquipmentDisposed` | net472 | `PubSubEvent<EquipmentDisposal>` |

**How one wire carries both formats.** A legacy app knows an event by its class's **full name**; a modern app or the
API by its `[Message("…")]` name. The body is JSON either way. So:

- The legacy apps receive the API's events through `Poc.Legacy.Events/FromApi/`: classes in namespace `Equipment`,
  named after the API's events (`Equipment.EquipmentCreated : PubSubEvent<Equipment.Messages.EquipmentCreated>`). The
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
| Delay after a change | ~2 s (outbox poll) | immediate |
| Survives an API crash mid-change | ✅ the event waits in the outbox | ❌ best effort |
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

- **Add a laptop in any window.** It appears everywhere, from the API's event: SignalR windows at once, RabbitMQ
  windows about two seconds later.
- **Send a note from net472 or net8.** Every net472 and net8 window logs it. The net10 windows log nothing.
- **Dispose of a laptop from net472.** The net8 window logs the disposal; the API logs
  `Equipment … disposed by net472 …; removed from inventory: True`; ~2 s later the row disappears from **every**
  window, net10 included — through the API's `EquipmentDeleted`, not the disposal.
- **Watch the management UI** (`http://localhost:15672`) → Exchanges → `Poc.Net472` → Bindings: the net8 queues, the
  other net472 queues and `cleanarch.api.main`, but no net10 queue.
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
- ❌ Using the legacy adapter in a NEW app. It exists so existing apps keep working unchanged.

**Modern format (net10)**

- ❌ Subscribing to a type the bus doesn't receive. Register it (`AddMessages`) or it never fires.
- ❌ Forgetting to dispose `IMessageSubscriber` subscriptions. Strong references: closed windows leak.
- ❌ Adding a legacy app's exchange to `Subscriptions`. That ends "API only".
- ❌ Publishing. No `Route<T>()`, no `IMessagePublisher`: modern apps only listen.

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

## Layout

```
Poc.Legacy.Events/   the legacy apps' PubSubEvent<T> events; FromApi/ = the API's events under their wire names
Poc.Shared/          EquipmentApi (HTTP), EquipmentList (load + apply), ApiOptions; SignalR/ (net8+ only). net472 + net8.0
Poc.Net472/          legacy app: App.xaml.cs (RemoteEventRegistry + RabbitMQServiceRouter), ViewModels/ (Subscribe, PublishRemote)
Poc.Net8/            the same app on .NET 8
Poc.Net10/           modern app: App.xaml.cs (Generic Host; RabbitMQ or SignalR), ViewModels/
Poc3.SignalR/        SignalR only, with presence
setup-exchanges.ps1  creates the four exchanges
```

On the API side: `src/Modules/Equipment/Equipment.Infrastructure/IntegrationEvents/EquipmentDisposed*.cs`, the
`Handle<>` line in `src/Api/CleanArch.Api/Program.cs`, and `Subscriptions` in its `appsettings.Development.json`.

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
