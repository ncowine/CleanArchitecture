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
    │                            └── Messaging.Prism    (optional: messages as Prism events)
    ▼
 Messaging.RabbitMQ   RabbitMQBus: one bus = two connections, a queue, a send buffer. Knows nothing about Prism or DI.
 Messaging.Abstractions   types every layer shares: MessageContext, PublishContext, IMessagingObserver, [Message]
```

Everything that touches RabbitMQ is in `RabbitMQBus`. The two front doors only turn their own style of API into
calls on it, and back.

## Starting up

**API and modern apps** (`Messaging.Hosting`):

```
services.AddMessaging(...)                 MessagingServiceCollectionExtensions.AddMessaging
  └─ messaging.AddBus("Main", section)     MessagingBuilder.AddBus
       ├─ named options for the bus        AddOptions<RabbitMQBusOptions>("Main").Bind(section)
       └─ the bus itself, created lazily   AddKeyedSingleton("Main", ... CreateBus)
host starts
  └─ MessagingHostedService.StartAsync
       ├─ registry.Validate()              MessagingRegistry: fails fast on unknown buses, missing routes, duplicate wire names
       └─ bus.Start(registrations)         RabbitMQBus.Start: starts the background loops (see Threads)
```

A desktop app without the .NET Generic Host uses `MessagingClient.Create(...)`, which builds the same services and
calls the hosted service's `StartAsync` itself.

**Legacy apps** (`Common.RabbitMQ`): `RabbitMQServiceRouter.AddBus(config)` makes one `RabbitMQService` per bus.
`RabbitMQService.Init` turns the `RabbitMQConfig` into `RabbitMQBusOptions` (`CreateOptions`), creates the
`RabbitMQBus` and starts it.

## Sending

**Buffered** (legacy apps, `IMessagePublisher`): returns at once, sent in the background.

```
Legacy: event.PublishRemote(payload)       RemotePubSubEventExtensions.PublishRemote
          └─ RabbitMQServiceRouter.Publish → RabbitMQService.Publish → bus.Enqueue(wire name, payload, routing key)
             (then raises the event locally too)
Modern: publisher.PublishAsync(message)    MessagePublisher.PublishAsync
          └─ looks up the route (which buses) → bus.Enqueue(...) on each

RabbitMQBus.Enqueue
  └─ Prepare: serialize now, pick routing key and ID, observer.OnPublishing (TelemetryObserver adds correlation-id, traceparent)
  └─ add to outstandingMessages, return
RabbitMQBus.OutstandingQueue               background loop
  └─ PublishMessage: frozen headers (event-type, source-id) + observer's headers → BasicPublishAsync
  └─ removed from the buffer only after the broker confirms; on failure it stays and is tried again
```

**Confirmed** (the API's outbox only): waits for the broker, buffers nothing.

```
Outbox processor → MessagingOutboxDispatcher.DispatchAsync      (src/BuildingBlocks.Outbox.Messaging)
  └─ CorrelationContext.Begin(row's correlation ID)
  └─ IConfirmedMessagePublisher.PublishConfirmedAsync            MessagePublisher
       └─ RabbitMQBus.PublishConfirmedAsync
            ├─ Prepare (as above)
            ├─ PublishMessage(mandatory: true), with a 30 s confirm timeout
            └─ on failure: AsBrokerUnavailable decides if it's the broker's fault → BrokerUnavailableException
  └─ BrokerUnavailableException → OutboxDeliveryDeferredException: the row waits, its attempts aren't used up
```

## Receiving

```
RabbitMQBus.KeepConnected(Consumer) → OpenConsumer
  └─ declares the exchange and queue, binds every subscribed wire name, BasicConsumeAsync(OnMessageReceived)

RabbitMQ.Client calls RabbitMQBus.OnMessageReceived for each delivery
  └─ Receive
       ├─ IsOwnEcho? → ack, skip                           (per-instance queues only)
       ├─ wire name from the event-type header (or the routing key)
       ├─ unknown wire name? → skip or dead-letter
       ├─ build MessageContext, observer.OnReceived
       └─ HandleWithRetries: deserialize → dispatcher.Dispatch(...), retrying on shared queues
  └─ Settle: ack, reject (= dead-letter) or nothing (stopping)

dispatcher.Dispatch is where the front doors differ (IInboundDispatcher):
  Legacy: PrismDispatcher.Dispatch → RemoteEventDescriptor.PublishLocal → GetEvent<T>().Publish(payload)
  Modern: ScopedDispatcher.Dispatch
            ├─ CorrelationContext.Begin(message's correlation ID)
            ├─ new DI scope → each handler: HandlerRegistration.Invoke → THandler.Handle(message, context, token)
            └─ each IMessageSink.Deliver:
                 MessageSubscriber   → the IMessageSubscriber.Subscribe callbacks for this type
                 EventAggregatorSink → GetEvent<MessageEvent<T>>().Publish(message)   (Messaging.Prism)
```

## Threads

| What | Runs on | Notes |
|---|---|---|
| `KeepConnected` ×2, `OutstandingQueue` | Thread-pool loops started by `Start` | The connection loops check every `ReconnectDelay` (5 s), the buffer loop every `OutstandingPollInterval` (100 ms) when idle. All exit when `Dispose` cancels the bus's token. |
| `OnMessageReceived`, so every handler, sink and Prism publish | A RabbitMQ.Client thread, **one message at a time per bus** (the client's default) | **Never the UI thread.** UI code must marshal: Prism `ThreadOption.UIThread`, or pass a `SynchronizationContext` to `IMessageSubscriber.Subscribe`. |
| `Enqueue`, `PublishAsync`, `Subscribe` | Whatever thread calls them | Safe from any thread: `subscriptions` and `outstandingMessages` are concurrent collections. |
| `PublishConfirmedAsync` | The outbox processor | Shares the publisher channel with `OutstandingQueue`. That's safe because RabbitMQ.Client 7 serializes publishes on a channel. |

Two places need care:

- **`consumerLock`** (in `RabbitMQBus`) makes sure a reconnect (`OpenConsumer`/`CloseConsumer`) and a binding change
  (`Subscribe`/`Unsubscribe` → `BindOrUnbind`) never use the consumer channel at the same time.
- **`Dispose`** cancels the loops, then closes both connections and waits at most 5 seconds. It runs the closing on
  the thread pool, so a UI thread calling `Dispose` can't deadlock. Messages still in the buffer are lost, and
  that's logged.

## .NET features you may not know

| Feature | Where | What it does here |
|---|---|---|
| `AsyncLocal<T>` | `CorrelationContext` | Like a static field, but each chain of async calls has its own value. The correlation ID set for one message is seen by everything that message's handlers call, across `await`s. Messages handled at the same time don't see each other's ID. |
| `ConfigureAwait(false)` | Every `await` in the libraries | "Don't come back to the caller's thread after this await." Library code never needs the UI thread, and this stops a WPF app that waits on a task from deadlocking. |
| `SemaphoreSlim` as a lock | `RabbitMQBus.consumerLock` | C# doesn't allow an `await` inside `lock { }`. `await consumerLock.WaitAsync()` … `Release()` in `finally` is the async version. |
| Linked token + `CancelAfter` | `RabbitMQBus.PublishConfirmedAsync` | One token that cancels when **either** the caller cancels **or** 30 s pass. Comparing the two tokens afterwards tells a timeout from a caller giving up. |
| Exception filters (`catch … when`) | `KeepConnected`, `HandleWithRetries`, `PublishConfirmedAsync` | The catch only applies when the condition is true. Otherwise the exception passes through untouched. |
| Keyed services | `MessagingBuilder.AddBus`, `GetRequiredKeyedService<RabbitMQBus>(name)` | Several singletons of one type, told apart by a key: here, the bus name. |
| Named options | `AddOptions<RabbitMQBusOptions>(name)`, `IOptionsMonitor<…>.Get(name)` | The same idea for settings: one `RabbitMQBusOptions` per bus. `PostConfigure` fills in the bus name and a default client name after binding. |
| `Interlocked.Exchange` | `SubscriptionToken.Dispose` | Swaps the unsubscribe action for null in one atomic step, so disposing twice, even from two threads, unsubscribes once. |
| Copy-on-write + `Volatile` | `MessageSubscriber` | Subscribing builds a **new** dictionary under a lock and swaps it in. Delivery reads the current one without locking, so a slow UI subscriber never blocks a new subscription. |
| `SynchronizationContext.Post` | `MessageSubscriber.Subscribe` | Queues the callback to run on the thread that owns the context: in WPF, the UI thread. |
| Reflection (`MethodInfo`, `MakeGenericType`) | `RemoteEventDescriptor`, `EventAggregatorSink` | Prism's `GetEvent<T>()` needs the type at compile time, but a received message's type is only known at run time. Reflection calls it with the run-time type, once per type, then reuses the result. |
| `extern alias baseline` | `tests/.../PublicApi`, `Broker/BaselineEndpoint` | Lets the tests load the frozen copy of the old library next to the new one, although both use the same namespaces. |

## Where to make common changes

| You want to | Change | Also update |
|---|---|---|
| Send a new message from the API | A class with `[Message("...")]` in the module's `*.Messages` project; `messaging.Route<T>()` in `Program.cs` | The receiving apps' `AddMessages(...)` |
| Add a bus setting | `RabbitMQBusOptions`, used in `RabbitMQBus` | `RabbitMQConfig` and `RabbitMQService.CreateOptions` if legacy apps need it; the README's configuration reference |
| Change what happens when a handler fails | `HandleWithRetries`, `FailedSettlement` | README "When things go wrong"; the `SharedQueueTests` |
| Add a header to outgoing messages | An `IMessagingObserver.OnPublishing` (see `TelemetryObserver`) | Nothing in the engine: observer headers are added on top of the frozen ones |
| Change reconnect behaviour | `KeepConnected`, `ReconnectDelay` | |

**Don't change without an ADR:** the frozen headers (`WireHeaders.MessageType`, `SourceId`), the JSON in
`tests/Common.RabbitMQ.Tests/Golden/*.json`, anything under `tests/Fixtures`, or the public API of
`Common.RabbitMQ`. Legacy apps depend on them byte for byte, and the tests will fail. See the rules in
[PROVENANCE.md](PROVENANCE.md#rules-that-came-with-the-code).

Run the tests against a real broker after any change: `RABBITMQ_TESTS_REQUIRED=1 dotnet test tests/Common.RabbitMQ.Tests`.
Both net472 and net8.0 must pass.
