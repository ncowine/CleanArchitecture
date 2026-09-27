# Messaging POCs: three ways for a modern WPF app to hear about changes

Three small WPF apps (.NET 8) that do the same job — show the API's equipment list and keep it current as it changes —
in three different ways. They all talk to the **real `CleanArch.Api`**, and they all receive the **same events**:
`EquipmentCreated`, `EquipmentUpdated` and `EquipmentDeleted` from `src/Modules/Equipment/Equipment.Messages`.

| App | Receives the API's events over | Through | Needs |
|---|---|---|---|
| **POC 1** `Poc1.PrismRabbitMq` | RabbitMQ (the API's outbox) | Prism's `IEventAggregator` (`MessageEvent<T>`, via `Messaging.Prism`) | API + RabbitMQ |
| **POC 2** `Poc2.PlainRabbitMq` | RabbitMQ (the API's outbox) | `IMessageSubscriber` (`Messaging.Hosting`), no Prism | API + RabbitMQ |
| **POC 3** `Poc3.SignalR` | SignalR (the API's hub) | A `HubConnection` | API only |

In all three, **changes go to the API over HTTP** (the Add / Delete buttons) and loading is over HTTP. Only the way
each app *listens* differs — which is the point of comparing them. Read the comments marked ✅ DO and ❌ DON'T.

This is a separate solution (`MessagingPocs.slnx`) with its own build settings, so it never affects the API's build.

---

## One change, both channels

```
            HTTP POST /equipment  (Swagger, curl, or any POC's "Add a laptop")
                         │
                 CreateEquipment handler  ── one EquipmentCreated ──┐
                         │                                          │
         ┌───────────────┴──────────────┐                           │
   _outbox.Enqueue(created)     _realtime.Publish(created)           │  both wait for the commit;
   (row saved WITH the change)  (buffered until the commit)          │  a rolled-back change sends nothing
         │                              │
   outbox processor, ~2 s later    right after the commit
   confirmed publish → RabbitMQ    SignalR hub, group "equipment"
         │                              │
   POC 1 and POC 2                 POC 3
```

Run all three and add a laptop: POC 3 shows it at once; POC 1 and 2 about two seconds later. The difference is the
trade-off: SignalR is immediate but best effort; the outbox is slightly later but survives an API crash.

How the API side is built, and why it doesn't break the architecture's rules:
[tutorial 67, chapters 8–12](../../tutorials/67-messaging-with-rabbitmq.md#8-step-1--turn-messaging-on-in-the-api).

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
| Moving parts in the app | Broker connection, messaging library, Prism aggregator | Broker connection, messaging library | One SignalR connection |
| Credentials on each desktop | A RabbitMQ account + the API key | A RabbitMQ account + the API key | The API key only |
| Network access needed | Broker (5672) and the API | Broker (5672) and the API | The API only |
| Receives events legacy apps publish | ✅ directly | ✅ directly | Only if the API forwards them |
| How soon after a change | ~2 s (outbox poll) | ~2 s (outbox poll) | Immediately |
| Survives the app being closed | ❌ events sent meanwhile are gone | ❌ same | ❌ same |
| Survives an API/broker restart | ✅ reconnects, then reloads | ✅ reconnects, then reloads | ✅ with the retry policy in the POC (not the default one) |
| Main trap | Prism's weak references drop lambda subscribers silently | Forgetting to register the message type, or to dispose subscriptions | Not re-joining groups after a reconnect |

None of the three guarantees delivery to a desktop, so **every one of them loads over HTTP when it starts and again
after every reconnect**, and uses events only to stay current in between (`Poc.Shared/EquipmentList.cs`).

---

## Running them

1. **RabbitMQ** on `localhost:5672` (for POC 1 and 2) — see
   [Running it on your machine](../../docs/messaging/README.md#running-it-on-your-machine).
2. **The API** in Development (it creates its SQLite databases, seeds the dev API key, and — because
   `appsettings.Development.json` has a `Messaging` section — publishes Equipment's events to RabbitMQ):
   ```powershell
   dotnet run --project src/Api/CleanArch.Api
   ```
3. **Build** the POCs: `dotnet build samples/MessagingPocs/MessagingPocs.slnx`
4. **Open the windows** (any number of copies of each):
   ```powershell
   samples\MessagingPocs\Poc1.PrismRabbitMq\bin\Debug\net8.0-windows\Poc1.PrismRabbitMq.exe
   samples\MessagingPocs\Poc2.PlainRabbitMq\bin\Debug\net8.0-windows\Poc2.PlainRabbitMq.exe
   samples\MessagingPocs\Poc3.SignalR\bin\Debug\net8.0-windows\Poc3.SignalR.exe
   ```

**Things to try**

- **Add a laptop in any window, or through Swagger.** Every window updates — the one that asked included — and it
  does so from the event, not from the button. POC 3 first; POC 1 and 2 about two seconds later.
- **Start a RabbitMQ POC before the API has ever run with messaging on.** It says it can't bind yet: the API owns the
  `CleanArch` exchange and creates it the first time it connects. Start the API and the window connects by itself.
- **Stop RabbitMQ, change something, start RabbitMQ.** The API keeps working (the change is saved; its event waits in
  the outbox without using up attempts). The RabbitMQ windows reconnect, reload — so they catch up — and then get
  the event when the outbox sends it.
- **Restart the API while windows are open.** POC 3 reconnects, **joins its group again**, reloads and carries on.
  Comment out the re-join in `EquipmentLiveUpdates` and try again: it says "Connected" and never updates again.
- **Watch the management UI** (`http://localhost:15672`, guest/guest) → Queues: each RabbitMQ window has its own
  temporary queue bound to `CleanArch` with the keys `Equipment.EquipmentCreated` / `Updated` / `Deleted`. It
  disappears when the window closes.

---

## The pitfalls, in one list

Each is explained where it happens in the code.

**All three**

- ❌ Publishing from a modern app, or writing through the hub. Changes go to the API over HTTP; the server publishes.
- ❌ Assuming the app will be told about everything. Load on start-up and after every reconnect; events only keep
  the list current.
- ❌ Loading before subscribing. A change in between is in neither the list nor the events.
- ❌ Updating bound collections from the thread an event arrives on. Move to the UI thread first.
- ❌ Applying events in a way that breaks if one arrives twice, or during a load. Make each event "set this state"
  (`EquipmentList` treats Created and Updated alike), and hold events back while a load is in flight.
- ❌ Adding a row locally after your own HTTP call succeeds. The event adds it; doing both shows it twice.
- ❌ A new `HttpClient` per call (socket exhaustion), or a factory-typed client held by a singleton expecting DNS
  refresh. Keep one client with a `PooledConnectionLifetime` (`Poc.Shared/EquipmentApi.cs`).
- ❌ Blocking on async shutdown on the UI thread. Hand it to the thread pool, or the app hangs on exit.

**RabbitMQ (POC 1 and 2)**

- ❌ Using the legacy adapter (`Common.RabbitMQ`, `PubSubEvent<T>` subclasses) in a new app. It exists for the .NET
  Framework apps.
- ❌ Subscribing to a message type the bus doesn't receive. Register it (`AddMessages`) or it never fires.
- ❌ *(POC 1)* Prism subscriptions with capturing lambdas. Weak references: they're collected and stop silently.
- ❌ *(POC 2)* Forgetting to dispose `IMessageSubscriber` subscriptions. Strong references: closed windows leak and keep
  handling messages.
- ❌ Reloading only at start-up. A desktop app's queue is deleted when its connection drops; reload on every reconnect.
- ❌ Giving desktops a broker account that can publish or reconfigure. Use a restricted one — noting that the messaging
  engine still opens a publisher connection and declares the app's own exchange, even in a receive-only app.

**SignalR (POC 3)**

- ❌ `WithAutomaticReconnect()` with no arguments. It gives up after four tries (~45 s) and the window silently stops
  updating. Use a policy that keeps trying.
- ❌ Expecting automatic reconnect to cover the first connection. It doesn't; retry `StartAsync` yourself.
- ❌ Forgetting that a reconnect is a new connection. Group membership is lost; join again.

---

## What the server still needs for POC 3 to be production-ready

| Gap | Today | Needed |
|---|---|---|
| Hub authentication | `PresenceHub` accepts anyone; the POC's `X-Api-Key` isn't checked | `[Authorize]` on the hub with the API-key scheme |
| Presence | Counts distinct user names; everyone is "anonymous" | Meaningful once the hub authenticates |
| IIS | — | Enable the **WebSocket Protocol** Windows feature on the server |
| Scale-out | One server (fine for now) | Redis backplane before a second API instance ([tutorial 65 §12](../../tutorials/65-real-time-notifications.md#12-scaling-past-one-instance)) |

(Named event contracts, previously on this list, are done: the hub now sends the `Equipment.Messages` classes.)

---

## Layout

```
Poc.Shared/            EquipmentApi (HTTP), EquipmentList (load + apply events), ApiOptions — shared by all three
Poc1.PrismRabbitMq/    App.xaml.cs (MessagingClient + Prism bridge), ViewModels/ (Prism subscriptions, reconnect reload)
Poc2.PlainRabbitMq/    App.xaml.cs (Generic Host), ViewModels/ (IMessageSubscriber, disposal, reconnect reload)
Poc3.SignalR/          App.xaml.cs, EquipmentLiveUpdates.cs (connection, retries, re-join), ViewModels/
```

POC 1 and 2 reference the real messaging projects in `src/Messaging`, and all three reference the API's real
`Equipment.Messages` — so what you see is how it behaves for real.

## Clean-up

The broker keeps the exchanges (`CleanArch`, `Poc.PrismApp`, `Poc.PlainApp`) and the API's durable queues
(`cleanarch.api.main`, `.dead-letter`) after everything closes; they're harmless. Delete them in the management UI if
you want a clean broker.

## How these were checked

Built with no warnings, then run for real against a local RabbitMQ 4.3.6 and the API in Development. All three
windows open at once, each loaded the list over HTTP; a laptop created through the API by an outside caller appeared
in POC 3 within a second and in POC 1 and 2 about two seconds later (the outbox poll); deleting it removed it from all
three; every window closed cleanly. Window contents were read back through Windows UI Automation. POC 3's reconnect —
re-joining its group after an API restart — was checked separately.
