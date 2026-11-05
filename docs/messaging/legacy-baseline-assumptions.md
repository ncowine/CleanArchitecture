# Legacy baseline: facts and assumptions

The real legacy messaging library isn't available to this repository. [ADR 0002](adr/0002-exchange-per-owner-and-configurable-routing.md) step 5 therefore uses a **model** of it:

- **Model:** `tests/Fixtures/LegacyModel/Legacy.RabbitMQ`
- **Example event assemblies:** `AppA.Events`, `AppB.Events`, and `Shared.Events` (netstandard2.0)
- **Tests:** `tests/Common.RabbitMQ.Tests/LegacyModel`

Each behaviour below is either a **fact** confirmed by the user or an **assumption** carried over from the replica. Every row has a test named after its ID. The tests run against a real broker and pass for the model as it stands.

**To check at work:** compare each assumption with the real library and mark it *confirmed* or *different*. To correct one, change the model, this row and the named test **together**. The other rows stay valid.

## Facts (confirmed)

| ID | Behaviour | Test |
|---|---|---|
| F1 | Events are plain `PubSubEvent<T>` classes, with no attribute and no special base class. Event assemblies don't reference the messaging library. Common events live in a netstandard2.0 project. | `LegacyModelRegistryTests.F1_*` |
| F2 | **One exchange per application.** An application publishes only to its own exchange. Another application receives those events by binding its queue to that exchange. | `LegacyModelSpecTests.F2_*` |
| F3 | Every application uses the default virtual host, `/`. | `LegacyModelRegistryTests.F3_VirtualHost_IsTheDefault` |
| F4 | Shared versions: RabbitMQ.Client 7.1.2, Prism.Core 8.1.97, Newtonsoft.Json 12.0.3. | Package versions in `Directory.Packages.props` |
| F5 | The **routing key** is the **publishing application's name**, which is also its exchange name (`AppA`), whatever the event. Confirmed 2026-11-05. | `LegacyModelSpecTests.F5_A3_A4_A5_PublishedMessage_OnTheWire` |
| F6 | The queue binds its **own** exchange **once**, keyed by the application's own name. Confirmed 2026-11-05. | `LegacyModelSpecTests.F6_F7_Bindings_OnePerExchange_KeyedByTheExchangeName` |
| F7 | For each **subscribed** exchange the queue gets **one** binding, keyed by that exchange's owner's name. So every event on a subscribed exchange arrives, and the receiver drops those it doesn't know. Confirmed 2026-11-05. | `F6_F7_…` |

## Assumptions (to check)

| ID | Assumed behaviour | Test | What to check in the real library |
|---|---|---|---|
| A1 | On connect, an application declares **its own exchange** as **topic**, **durable**, not auto-delete. | `A1_OwnExchange_IsDeclaredByItsApp_AsDurableTopic` | The `ExchangeDeclare` call: type, durability, auto-delete, arguments. |
| A2 | *(replaced by fact F5)* | | |
| A3 | Headers: `event-type` (full type name) and `source-id` (a random ID per process). | `F5_A3_A4_A5_…` | Header names and values. With the app name as the routing key, `event-type` is the only thing that says which event a message is. |
| A4 | Properties: content type `application/json`, a new message ID, app ID = client name, a timestamp. | `F5_A3_A4_A5_…` | The `BasicProperties` that are set. |
| A5 | Body: the payload alone, as JSON with Newtonsoft default settings. No envelope or wrapper. | `F5_A3_A4_A5_…` (compared with `Golden/compat-payload.json`) | Serializer settings, and whether there's a wrapper object. |
| A6 | Publishing goes through an in-memory outstanding queue, sent in the background with publisher confirms. | *(not observable on the wire)* | Whether the real library buffers messages and uses confirms. |
| A7 | **One queue per running process**, named `clientname.instanceid`, exclusive, auto-delete, non-durable. | `A7_Queue_IsExclusivePerProcess_AndRemovedWhenTheAppStops` | The `QueueDeclare` call: name, flags, arguments. |
| A8 | *(replaced by fact F6)* | | |
| A9 | *(replaced by fact F7)* The subscriptions are a list of exchange names in configuration. | | Where the subscribed exchanges are configured. **A sample App.config would answer this.** |
| A10 | A received message's event is found from the `event-type` header among the registered events. A message without it, or naming an unknown event, is ignored. | `A10_EventIsResolvedFromTheHeader_WithoutItTheMessageIsIgnored` | How the receiver works out which event a message is. |
| A11 | An application drops its **own** messages when they come back (matching `source-id`). | `F2_A11_A15_…` | Whether the real library filters its own messages, and how. |
| A12 | Messages are acknowledged manually and **always** acknowledged, even when a subscriber throws. The error is logged. | `A12_FailingSubscriber_MessageIsAcked_AndNotRedelivered` | Ack mode (auto or manual) and error handling. |
| A13 | Reconnecting is done by the library's own loops, not by the client's automatic recovery. | *(not observable on the wire)* | `AutomaticRecoveryEnabled` and how reconnects work. |
| A14 | *(merged into F4)* | | |
| A15 | `PublishRemote` raises the event for **local** subscribers synchronously, as well as sending it. | `F2_A11_A15_…` | Whether local subscribers also get the event. |
| A16 | A subscriber **declares the exchanges it subscribes to** (as durable topics) before binding, so it can start before their owners. | `A16_SubscribedExchange_IsDeclaredByTheSubscriber_BeforeItsOwnerStarts` | Whether subscribers declare other applications' exchanges, or only bind (which fails if the exchange doesn't exist yet). |

## Why A16 matters

ADR 0002 section 2 says subscribers only **check** other applications' exchanges and never declare them. If the real library declares them (A16), mixing old and new code has consequences:

- **Changing an exchange's type needs coordination.** An old subscriber that re-declares an exchange as a topic breaks when its owner changes the type. Every old subscriber has to be updated at the same time.
- **Startup order.** A new subscriber has to cope with an old owner that hasn't declared its exchange yet, and the reverse.

Step 6 proves the first point on a broker: when an owner declares its exchange as `direct`, a legacy subscriber can't connect (`CoreTopologyTests.A16_Consequence_LegacySubscriberCannotJoinANonTopicExchange`). Until A16 is confirmed, keep exchanges that legacy applications subscribe to as `topic`.

Confirming A16 is high on the list.

## Why F5 to F7 matter

Bindings are per exchange, not per event, so the broker's binding count grows with clients × exchanges, not clients × events. A load test on 2026-11-05 (500 clients, 200 events each, 3 exchanges) showed why that matters: one binding per event meant 300,000 bindings and crashed a local broker, while one per exchange meant 1,500 and started in under 2 seconds. The cost is that every client receives every event on the exchanges it subscribes to and drops the ones nobody listens for.

The new libraries follow the same rule (ADR 0002, section 2), so mixing old and new apps adds no bindings. The frozen baseline in `tests/Fixtures/Common.RabbitMQ.Baseline` predates this and routes by event name, so it only takes part in checks of its own headers and body.

## Still unknown

- The real library's public API beyond `PublishRemote`, `IRabbitMQService` and `IEventAggregator`: class names, the `Init` signature, how it's registered in DI.
- The real App.config format for the own exchange and for subscriptions.
- Whether any application publishes to **another** application's exchange.
