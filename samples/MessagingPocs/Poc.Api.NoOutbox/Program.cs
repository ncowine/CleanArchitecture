using Equipment.Messages;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Poc.Api.NoOutbox;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// An API that publishes without an outbox and subscribes to events, using the messaging library as it is.
//
//   Sending:
//   POST /equipment/confirmed   IConfirmedMessagePublisher: waits for the broker's confirm, so the caller knows.
//   POST /equipment/buffered    IMessagePublisher: buffers in memory and returns, as the legacy apps do.
//
//   Receiving:
//   EquipmentDisposedHandler    A handler class for EquipmentDisposed, from the net472 app's exchange (Subscriptions in
//                               appsettings.json). Use one for real work: it gets a new DI scope per message.
//   Subscribe, below            The same without a class, for EquipmentFlagged: a legacy event whose payload is a tuple.
//                               For small jobs that need no scoped services.
//   Receiving works the same with or without an outbox.
//
// WHAT YOU GIVE UP WITHOUT THE OUTBOX
//   A change and its event can't be saved together. If this were saving to a database:
//   - confirmed: the save can succeed and the publish fail. The event is gone unless the caller retries.
//   - buffered:  the same, and a crash or restart also loses whatever is still in memory.
//   Use this for events that are fine to lose now and then. Use the outbox for events other apps rely on (ADR 0003).
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
app.Services.GetRequiredService<IMessageSubscriber>().Subscribe<EquipmentFlagged>((message, context, cancellationToken) =>
{
    app.Logger.LogInformation("Equipment {Name} ({EquipmentId}) flagged", message.Name, message.Id);

    return Task.CompletedTask;
});

app.MapPost("/equipment/confirmed", async (CreateEquipmentRequest request, IConfirmedMessagePublisher publisher, CancellationToken cancellationToken) =>
{
    EquipmentCreated created = ToEvent(request);

    // A real API saves its change here, before publishing.
    try
    {
        // Null message ID: the bus generates one.
        await publisher.PublishConfirmedAsync(created, null, cancellationToken);
    }
    catch (BrokerUnavailableException ex)
    {
        // Tell the caller. Nothing is kept here, so nobody else will send this event later.
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Created($"/equipment/{created.Id}", created);
});

app.MapPost("/equipment/buffered", (CreateEquipmentRequest request, IMessagePublisher publisher) =>
{
    EquipmentCreated created = ToEvent(request);

    // Succeeds with the broker down too: the bus holds the message in memory and sends it after it reconnects.
    publisher.Publish(created);

    return Results.Accepted($"/equipment/{created.Id}", created);
});

app.Run();

static EquipmentCreated ToEvent(CreateEquipmentRequest request) => new()
{
    Id = Guid.NewGuid(),
    Name = request.Name,
    Category = request.Category,
    AssetTag = request.AssetTag,
    Status = "Available",
};
