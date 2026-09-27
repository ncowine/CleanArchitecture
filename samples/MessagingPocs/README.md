# Messaging POCs: three ways for a modern WPF app to hear about changes

Three small WPF apps (.NET 8) that do the same job — show a list and keep it current as things change elsewhere — in
three different ways. Run them side by side, read the comments marked ✅ DO and ❌ DON'T, and pick deliberately.

| App | Gets changes from | Through | Needs |
|---|---|---|---|
| **POC 1** `Poc1.PrismRabbitMq` | RabbitMQ | Prism's `IEventAggregator` (`MessageEvent<T>`, via `Messaging.Prism`) | RabbitMQ, `Poc.ServerSimulator` |
| **POC 2** `Poc2.PlainRabbitMq` | RabbitMQ | `IMessageSubscriber` (`Messaging.Hosting`), no Prism | RabbitMQ, `Poc.ServerSimulator` |
| **POC 3** `Poc3.SignalR` | The API | SignalR for updates, HTTP for loading and changes | `CleanArch.Api` |

All three only **listen**. Changes go to the server; that rule is the same in every POC
([ADR 0003](../../docs/messaging/adr/0003-server-publishes-through-the-outbox.md)).

This is a separate solution (`MessagingPocs.slnx`) with its own build settings, so it never affects the API's build.

---

## Which one should a new app use?

```
Do the events the app needs all come from the API?
├── Yes → POC 3 (SignalR). The default for new modern apps.
└── No, some are published only by legacy apps
    ├── App is being migrated from .NET Framework and is built on Prism → POC 1
    └── Otherwise → POC 2
```

| | POC 1 — RabbitMQ + Prism | POC 2 — RabbitMQ, no Prism | POC 3 — SignalR |
|---|---|---|---|
| Moving parts in the app | Broker connection, messaging library, Prism aggregator | Broker connection, messaging library | One SignalR connection, one HttpClient |
| Credentials on each desktop | A RabbitMQ account | A RabbitMQ account | The API key it already has |
| Network access needed | Broker (5672) | Broker (5672) | The API only |
| Receives events legacy apps publish | ✅ directly | ✅ directly | Only if the API forwards them |
| Survives the app being closed | ❌ events sent meanwhile are gone | ❌ same | ❌ same |
| Survives a server/broker restart | ✅ reconnects by itself | ✅ reconnects by itself | ✅ with the retry policy in the POC (not the default one) |
| Main trap | Prism's weak references drop lambda subscribers silently | Forgetting to register the message type, or to dispose subscriptions | Not re-joining groups after a reconnect |

None of the three guarantees delivery to a desktop, so **every one of them loads current state when it starts** (and
POC 3 again after each reconnect), and uses events only to stay current in between.

---

## Running them

### POC 1 and POC 2

1. RabbitMQ running on `localhost:5672` (see [Running it on your machine](../../docs/messaging/README.md#running-it-on-your-machine)).
2. Build: `dotnet build samples/MessagingPocs/MessagingPocs.slnx`
3. Start the simulator — it plays the API — and leave it open:
   ```powershell
   dotnet run --project samples/MessagingPocs/Poc.ServerSimulator
   ```
   Press **Enter** to send an event, **q** to quit. `-- --send 3` sends three and exits.
4. Start the windows (as many copies as you like; each copy gets every event):
   ```powershell
   samples\MessagingPocs\Poc1.PrismRabbitMq\bin\Debug\net8.0-windows\Poc1.PrismRabbitMq.exe
   samples\MessagingPocs\Poc2.PlainRabbitMq\bin\Debug\net8.0-windows\Poc2.PlainRabbitMq.exe
   ```

**Things to try**

- **Start a window before the simulator has ever run.** It says it's waiting for the server's exchange, and connects on
  its own once the simulator starts. Subscribers only *check* for another app's exchange; only its owner creates it.
- **Send an event while no window is open, then open one.** It never sees that event. A desktop app's queue exists
  only while the app runs — which is why real apps load current state on start-up.
- **Stop RabbitMQ and start it again** (`rabbitmqctl.bat stop`, then start the server). Windows show "retrying" and
  reconnect by themselves.
- **Watch the management UI** (`http://localhost:15672`, guest/guest) → Queues: each open window has its own temporary
  queue, bound to `Poc.Server` with the key `Poc.EquipmentStatusChanged`, and it disappears when the window closes.

### POC 3

1. Start the API in Development (it creates its SQLite databases and seeds the dev API key):
   ```powershell
   dotnet run --project src/Api/CleanArch.Api
   ```
2. Start two copies of the window:
   ```powershell
   samples\MessagingPocs\Poc3.SignalR\bin\Debug\net8.0-windows\Poc3.SignalR.exe
   ```

**Things to try**

- **Add a laptop in one window.** The request goes over HTTP; the row appears in *both* windows when the API's
  `EquipmentCreated` event arrives — including the window that asked. Swagger (`/swagger`) or `curl` work too: every
  window updates, whoever made the change.
- **Start the window before the API.** It keeps retrying the first connection (automatic reconnect doesn't cover that).
- **Restart the API while windows are open.** They show "reconnecting", reconnect, **join their group again**, reload,
  and carry on receiving. Comment out the re-join in `EquipmentLiveUpdates` and try again: the window says "Connected"
  and never updates again.

---

## The pitfalls, in one list

Each is explained where it happens in the code.

**All three**

- ❌ Publishing from a modern app. Changes go to the API over HTTP; the server publishes.
- ❌ Assuming the app will be told about everything. Load current state on start-up (and, for SignalR, after every
  reconnect); events only keep it current.
- ❌ Updating bound collections from the thread an event arrives on. Messages arrive on background threads; move
  to the UI thread first.
- ❌ Applying events in a way that breaks if one arrives twice or late. Make each event "set this state", and ignore
  one older than what's shown.
- ❌ Blocking on async shutdown on the UI thread (`StopAsync().Wait()`). Hand it to the thread pool, or the app hangs on
  exit.

**RabbitMQ (POC 1 and 2)**

- ❌ Using the legacy adapter (`Common.RabbitMQ`, `PubSubEvent<T>` subclasses) in a new app. It exists for the .NET
  Framework apps.
- ❌ Subscribing to a message type the bus doesn't receive. Register it (`AddMessages`) or it never fires.
- ❌ Subscribing after the connection starts. Build view models (and their subscriptions) first.
- ❌ *(POC 1)* Prism subscriptions with capturing lambdas. Weak references: they're collected and stop silently.
- ❌ *(POC 2)* Forgetting to dispose `IMessageSubscriber` subscriptions. Strong references: closed windows leak and keep
  handling messages.
- ❌ Giving desktops a broker account that can publish or reconfigure. Use a restricted one. Note that the messaging
  engine still opens a publisher connection and declares the app's own exchange, even in a receive-only app — the
  window's log shows it — so the account needs permission for that one exchange.
- ⚠ A server that sends before any subscriber has bound its queue loses those messages: the broker drops messages no
  queue is bound for. Here, the simulator's first event after its first-ever start goes nowhere.

**SignalR (POC 3)**

- ❌ `WithAutomaticReconnect()` with no arguments. It gives up after four tries (~45 s) and the window silently stops
  updating. Use a policy that keeps trying.
- ❌ Expecting automatic reconnect to cover the first connection. It doesn't; retry `StartAsync` yourself.
- ❌ Forgetting that a reconnect is a new connection. Group membership is lost; join again.
- ❌ Loading before joining. A change in between is in neither the list nor the events.
- ❌ Adding a row locally after your own HTTP call succeeds. The event adds it; doing both shows it twice.
- ❌ Writing through the hub. Every change goes through the API's HTTP endpoints (validation, auth, audit).
- ❌ A new `HttpClient` per call (socket exhaustion), or a factory-typed client held by a singleton expecting DNS
  refresh. Keep one client with a `PooledConnectionLifetime`.

---

## What the server still needs for POC 3 to be production-ready

These are server-side changes, outside these POCs, to be planned as ADR 0004:

| Gap | Today | Needed |
|---|---|---|
| Hub authentication | `PresenceHub` accepts anyone; the POC's `X-Api-Key` isn't checked | `[Authorize]` on the hub with the API-key scheme |
| Event contracts | Anonymous objects (`new { id, name, … }`) in the handlers | Named event classes in `Equipment.Contracts`, so renaming a field is a visible, reviewable change |
| Presence | Counts distinct user names; everyone is "anonymous" | Meaningful once the hub authenticates |
| IIS | — | Enable the **WebSocket Protocol** Windows feature on the server |
| Scale-out | One server (fine for now) | Redis backplane before a second API instance ([tutorial 65 §12](../../tutorials/65-real-time-notifications.md#12-scaling-past-one-instance)) |

---

## Layout

```
Poc.Contracts/         EquipmentStatusChanged — the [Message] class POC 1 and 2 receive
Poc.ServerSimulator/   console app that plays the server: owns the "Poc.Server" exchange and publishes
Poc1.PrismRabbitMq/    App.xaml.cs (wiring + when to use it), ViewModels/ (subscribing the Prism way)
Poc2.PlainRabbitMq/    App.xaml.cs (Generic Host wiring), ViewModels/ (IMessageSubscriber, disposal)
Poc3.SignalR/          App.xaml.cs, EquipmentLiveUpdates.cs (connection, retries, re-join),
                       EquipmentApi.cs (HTTP), ViewModels/ (load/event ordering)
```

POC 1 and 2 reference the real messaging projects in `src/Messaging` — the same code the API and the legacy adapter
use — so what you see here is how it behaves for real.

## Clean-up

The broker keeps the POCs' exchanges (`Poc.Server`, `Poc.PrismApp`, `Poc.PlainApp`) after they close; they're harmless.
To remove them: management UI → Exchanges → the exchange → Delete.

## How these were checked

Built with no warnings, then run for real against a local RabbitMQ 4.3.6 and the API:

- POC 1 and 2 started before the simulator waited for its exchange, then connected by themselves; each got its own
  temporary queue bound to `Poc.EquipmentStatusChanged`; five simulator events became three rows (one per asset) in
  each window, read back through Windows UI Automation; both closed without hanging and their queues were deleted.
- POC 3 loaded the list over HTTP; a laptop created through the API by someone else appeared in the window within two
  seconds without a reload, and disappeared again when deleted; after the API was restarted mid-session, the
  connection came back, re-joined the group and kept receiving.
