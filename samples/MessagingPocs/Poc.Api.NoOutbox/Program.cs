using Equipment.Messages;
using Messaging;
using Messaging.Hosting;
using Poc.Api.NoOutbox;
using Poc.Shapes;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// An API that publishes without an outbox and subscribes to events, using the messaging library as it is.
//
//   Sending:
//   POST /equipment/buffered    IMessagePublisher: buffers in memory and returns, as the legacy apps do.
//
//   Receiving:
//   EquipmentDisposedHandler    A handler class for EquipmentDisposed, from the net472 app's exchange (Subscriptions in
//                               appsettings.json). Use one for real work: it gets a new DI scope per message.
//   Subscribe, below            The same without a class, for EquipmentFlagged: a legacy event whose payload is a tuple.
//                               For small jobs that need no scoped services.
//   LogShape, below             The ten POC shape events (Poc.Shapes) from the legacy apps, logged as they arrive.
//   Receiving works the same with or without an outbox.
//
// WHAT YOU GIVE UP WITHOUT AN OUTBOX
//   A change and its event can't be saved together. The save can succeed and the publish be lost: a crash or restart
//   loses whatever is still in memory. The API makes the same trade-off.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The queue is bound once per message type received: every IMessageHandler<T> AddHandlers finds, and every type passed to
// Subscribe below.
builder.Services.AddMessaging(messaging => messaging
    .AddBus("Main", builder.Configuration.GetSection("Messaging:Buses:Main"))
    .UseDefaultBus(builder.Configuration["Messaging:DefaultBus"])
    .AddHandlers(typeof(EquipmentDisposedHandler).Assembly));

WebApplication app = builder.Build();

// ✅ DO subscribe before app.Run(), which starts the bus. A message that arrives with no subscriber is acknowledged and
//    lost, with no log, and that includes messages waiting in the durable queue from while the API was stopped.
// ❌ DON'T use scoped services directly here: there's no scope. Create one with app.Services.CreateAsyncScope().
// An exception fails the message, as in a handler class: it's retried, then dead-lettered.
IMessageSubscriber subscriber = app.Services.GetRequiredService<IMessageSubscriber>();
// The net472 app sends flags with the "equipment" routing key: subscribing with it binds that key on its exchange.
subscriber.Subscribe<EquipmentFlagged>((message, context, cancellationToken) =>
{
    app.Logger.LogInformation("Equipment {Name} ({EquipmentId}) flagged", message.Name, message.Id);

    return Task.CompletedTask;
}, new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" });

LogShape<StatusChanged>(subscriber, app.Logger);
LogShape<MaintenanceScheduled>(subscriber, app.Logger);
LogShape<CostRecorded>(subscriber, app.Logger);
LogShape<WarrantyChecked>(subscriber, app.Logger);
LogShape<TagsChanged>(subscriber, app.Logger);
LogShape<LocationMoved>(subscriber, app.Logger);
LogShape<AttributesChanged>(subscriber, app.Logger);
LogShape<UsageReported>(subscriber, app.Logger);
LogShape<BatchCounted>(subscriber, app.Logger);
LogShape<AuditRequested>(subscriber, app.Logger);

app.MapPost("/equipment/buffered", (CreateEquipmentRequest request, IMessagePublisher publisher) =>
{
    EquipmentCreated created = ToEvent(request);

    // Succeeds with the broker down too: the bus holds the message in memory and sends it after it reconnects.
    publisher.Publish(created);

    return Results.Accepted($"/equipment/{created.Id}", created);
});

app.Run();

static void LogShape<TMessage>(IMessageSubscriber subscriber, ILogger logger)
{
    subscriber.Subscribe<TMessage>((message, context, cancellationToken) =>
    {
        logger.LogInformation("{Event} from {Exchange}: {Message}", typeof(TMessage).Name, context.Exchange, message);
        return Task.CompletedTask;
    });
}

static EquipmentCreated ToEvent(CreateEquipmentRequest request) => new()
{
    Id = Guid.NewGuid(),
    Name = request.Name,
    Category = request.Category,
    AssetTag = request.AssetTag,
    Status = "Available",
};
