# Reading the messaging code

The [README](README.md) explains *what* the messaging does: exchanges, queues, wire names, retries. This page is
for changing it. It shows where each step lives in the code (file and method), which threads run what, and the
.NET features you may not have met before. Read [How a message travels](README.md#how-a-message-travels) first.

All paths are under `src/`. `RabbitMQBus` means `Messaging/Messaging.RabbitMQ/RabbitMQBus.cs`.

## The three layers

```
 Your app
    │
    ├── Legacy WPF app ──────► Common.RabbitMQ          (the old Prism API, frozen)
    ├── API / modern app ────► Messaging.Hosting        (AddMessaging, IMessagePublisher, IMessageSubscriber)
    ▼
 Messaging.RabbitMQ   RabbitMQBus: one bus = two connections, a queue, a send buffer. Knows nothing about Prism or DI.
 Messaging.Abstractions   types every layer shares: MessageContext, [Message]
```

Everything that touches RabbitMQ is in `RabbitMQBus`. The two front doors only turn their own style of API into
calls on it, and back.

## Starting up

**API and modern apps** (`Messaging.Hosting`):

```
services.AddMessaging(configuration)       MessagingServiceCollectionExtensions.AddMessaging
  ├─ checks Messaging:Buses and DefaultBus throws at once: no buses, no default, or a default that isn't a bus
  └─ one bus per Messaging:Buses entry     AddBus (private)
       ├─ named options for the bus        AddOptions<RabbitMQBusOptions>("Main").Bind(section)
       └─ the bus itself, created lazily   AddKeyedSingleton("Main", ... new RabbitMQBus)
host starts
  └─ every hosted service's StartAsync     subscribe here (IMessageSubscriber)
  └─ MessagingHostedService.StartedAsync
       └─ bus.Start()                      RabbitMQBus.Start: starts the background loops (see Threads)
```

Every modern app runs on the .NET Generic Host, which starts the hosted service. (`MessagingClient`, for apps without
a host, was removed on 2026-11-10.)

**Legacy apps** (`Common.RabbitMQ`): `RabbitMQServiceRouter.FromConfiguration` makes one `RabbitMQService` per `Messaging:Buses` entry (its only way in).
`RabbitMQService.Init` turns the `RabbitMQConfig` into `RabbitMQBusOptions` (`CreateOptions`), creates the
`RabbitMQBus` and starts it.

## Sending

**Buffered** (legacy apps, `IMessagePublisher`): returns at once, sent in the background.

```
Legacy: event.PublishRemote(payload)       RemotePubSubEventExtensions.PublishRemote
          └─ RabbitMQServiceRouter.Publish → RabbitMQService.Publish → bus.Enqueue(wire name, payload, routing key or null for the exchange name)
             (then raises the event locally too)
Modern: publisher.Publish(message)         MessagePublisher.Publish
          └─ the bus in PublishOptions, or the default bus → bus.Enqueue(...)

RabbitMQBus.Enqueue
  └─ Prepare: serialize now, pick routing key and ID
  └─ add to outstandingMessages, return
RabbitMQBus.OutstandingQueueAsync               background loop
  └─ PublishMessageAsync: frozen headers (event-type, source-id) → BasicPublishAsync
  └─ removed from the buffer only after the broker confirms; on failure it stays and is tried again
```

**The API** uses the same buffered path, after the commit:

```
EquipmentEventsBehavior.Handle               (src/Modules/Equipment/Equipment.Infrastructure/Behaviors)
  └─ next(): handler collects events in EquipmentEvents, TransactionBehavior commits
  └─ for each collected event: IMessagePublisher.Publish → RabbitMQBus.Enqueue (as above)
```

## Receiving

```
RabbitMQBus.KeepConnectedAsync(Consumer) → OpenConsumerAsync
  └─ declares the exchange and queue, binds every subscribed wire name, BasicConsumeAsync(OnMessageReceivedAsync)

RabbitMQ.Client calls RabbitMQBus.OnMessageReceivedAsync for each delivery
  └─ ReceiveAsync
       ├─ IsOwnEcho? → ack, skip                           (per-instance queues only)
       ├─ wire name from the event-type header (the routing key is only the publisher's name)
       ├─ nothing subscribed to that wire name? → ack and skip (normal: the queue gets the whole exchange)
       ├─ build MessageContext
       └─ HandleWithRetriesAsync: deserialize → dispatcher.DispatchAsync(...), retrying on shared queues
  └─ SettleAsync: ack, reject (= dead-letter) or nothing (stopping: also when a handler fails because the bus is shutting down)

dispatcher.DispatchAsync is where the front doors differ (IInboundDispatcher):
  Legacy: PrismDispatcher.DispatchAsync → RemoteEventDescriptor.PublishLocal → GetEvent<T>().Publish(payload)
  Modern: MessageSubscriber.DispatchAsync → the IMessageSubscriber.Subscribe callbacks for this type, in order
```

## Threads

| What | Runs on | Notes |
|---|---|---|
| `KeepConnectedAsync` ×2, `OutstandingQueueAsync` | Thread-pool loops started by `Start` | The connection loops check every `ReconnectDelay` (5 s), the buffer loop every `OutstandingPollInterval` (100 ms) when idle. All exit when `Dispose` cancels the bus's token. |
| `OnMessageReceivedAsync`, so every handler, sink and Prism publish | A RabbitMQ.Client thread, **one message at a time per bus** (the client's default) | **Never the UI thread.** UI code must marshal: Prism `ThreadOption.UIThread`, or pass a `SynchronizationContext` to `IMessageSubscriber.Subscribe`. |
| `Enqueue`, `Publish`, `Subscribe` | Whatever thread calls them | Safe from any thread: `subscriptions` and `outstandingMessages` are concurrent collections. |

Two places need care:

- **`consumerLock`** (in `RabbitMQBus`) makes sure a reconnect (`OpenConsumerAsync`/`CloseConsumerAsync`) and a binding change
  (`Subscribe`/`Unsubscribe` → `BindOrUnbind`) never use the consumer channel at the same time.
- **`Dispose`** cancels the loops, then closes both connections and waits at most 5 seconds. It runs the closing on
  the thread pool, so a UI thread calling `Dispose` can't deadlock. Messages still in the buffer are lost, and
  that's logged.

## .NET features you may not know

| Feature | Where | What it does here |
|---|---|---|
| `ConfigureAwait(false)` | Every `await` in the libraries | "Don't come back to the caller's thread after this await." Library code never needs the UI thread, and this stops a WPF app that waits on a task from deadlocking. |
| `SemaphoreSlim` as a lock | `RabbitMQBus.consumerLock` | C# doesn't allow an `await` inside `lock { }`. `await consumerLock.WaitAsync()` … `Release()` in `finally` is the async version. |
| Exception filters (`catch … when`) | `KeepConnectedAsync`, `HandleWithRetriesAsync` | The catch only applies when the condition is true. Otherwise the exception passes through untouched. |
| Keyed services | `AddMessaging` (`AddKeyedSingleton`), `GetRequiredKeyedService<RabbitMQBus>(name)` | Several singletons of one type, told apart by a key: here, the bus name. |
| Named options | `AddOptions<RabbitMQBusOptions>(name)`, `IOptionsMonitor<…>.Get(name)` | The same idea for settings: one `RabbitMQBusOptions` per bus. `PostConfigure` fills in the bus name and a default client name after binding. |
| `Interlocked.Exchange` | `SubscriptionToken.Dispose` | Swaps the unsubscribe action for null in one atomic step, so disposing twice, even from two threads, unsubscribes once. |
| Copy-on-write + `Volatile` | `MessageSubscriber` | Subscribing builds a **new** dictionary under a lock and swaps it in. Delivery reads the current one without locking, so a slow UI subscriber never blocks a new subscription. |
| `SynchronizationContext.Post` | `MessageSubscriber.Subscribe` | Queues the callback to run on the thread that owns the context: in WPF, the UI thread. |
| Reflection (`MethodInfo`, `MakeGenericType`) | `RemoteEventDescriptor`, `EventAggregatorSink` | Prism's `GetEvent<T>()` needs the type at compile time, but a received message's type is only known at run time. Reflection calls it with the run-time type, once per type, then reuses the result. |
| `extern alias baseline` | `tests/.../PublicApi`, `Broker/BaselineEndpoint` | Lets the tests load the frozen copy of the old library next to the new one, although both use the same namespaces. |

## Where to make common changes

| You want to | Change | Also update |
|---|---|---|
| Send a new message from the API | A class with `[Message("...")]` in the module's `*.Messages` project (the outbox picks it up from there) | The receiving apps' `Subscribe<T>(...)` |
| Add a bus setting | `RabbitMQBusOptions`, used in `RabbitMQBus` | `RabbitMQConfig` and `RabbitMQService.CreateOptions` if legacy apps need it; the README's configuration reference |
| Change what happens when a handler fails | `HandleWithRetriesAsync`, `FailedSettlement` | README "When things go wrong"; the `SharedQueueTests` |
| Change reconnect behaviour | `KeepConnectedAsync`, `ReconnectDelay` | |

**Don't change without an ADR:** the frozen headers (`WireHeaders.MessageType`, `SourceId`), the JSON in
`tests/Common.RabbitMQ.Tests/Golden/*.json`, anything under `tests/Fixtures`, or the public API of
`Common.RabbitMQ`. Legacy apps depend on them byte for byte, and the tests will fail. See the rules in
[PROVENANCE.md](PROVENANCE.md#rules-that-came-with-the-code).

Run the tests against a real broker after any change: `RABBITMQ_TESTS_REQUIRED=1 dotnet test tests/Common.RabbitMQ.Tests`.
Both net472 and net8.0 must pass.
