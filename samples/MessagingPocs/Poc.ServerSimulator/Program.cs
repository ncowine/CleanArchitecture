using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Poc.Contracts;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// Plays the part of the server for the RabbitMQ POCs.
//
// It owns the exchange "Poc.Server" (every application owns one exchange, named after it, and publishes only there),
// and sends EquipmentStatusChanged events to it. POC 1 and POC 2 bind their queues to this exchange.
//
// In the real system this is CleanArch.Api, and it publishes differently (docs/messaging/adr/0003):
//   handler → IOutbox.Enqueue(event)   (same transaction as the change)
//           → outbox processor → MessagingOutboxDispatcher → IConfirmedMessagePublisher → RabbitMQ
// This simulator has no database, so it calls IConfirmedMessagePublisher directly: the same publish the outbox
// relay uses, which waits for the broker's confirm and throws instead of buffering.
//
// ❌ DON'T copy this shortcut into the API. Without the outbox, a crash between "saved the change" and "published the
//    event" loses the event for good.
// ❌ DON'T use IMessagePublisher.PublishAsync on the server either: it only buffers, so a restart loses what's queued.
//
// Usage:  dotnet run                  press Enter to send an event, q to quit
//         dotnet run -- --send 3      send 3 events and exit (for scripts)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddMessaging(messaging => messaging
    .AddBus("Main", builder.Configuration.GetSection("Messaging:Buses:Main"))
    // A route is what makes a message publishable: which bus, and which routing key (default: the wire name).
    .Route<EquipmentStatusChanged>());

using IHost host = builder.Build();
await host.StartAsync();

RabbitMQBus bus = host.Services.GetRequiredKeyedService<RabbitMQBus>("Main");
IConfirmedMessagePublisher publisher = host.Services.GetRequiredService<IConfirmedMessagePublisher>();

// Connecting happens in the background. The publisher declares the "Poc.Server" exchange when it connects, so the
// POC windows (which only *check* that it exists) start receiving once this line has passed.
Console.WriteLine("Connecting to RabbitMQ...");
for (int waited = 0; !bus.IsPublisherConnected; waited++)
{
    if (waited == 100)
    {
        Console.WriteLine("Still not connected after 10 s. Is RabbitMQ running on localhost:5672?");
    }

    await Task.Delay(100);
}

Console.WriteLine("Connected. Exchange 'Poc.Server' is declared.");

string[] assetTags = ["LAP-001", "LAP-002", "MON-010"];
string[] statuses = ["InStock", "Assigned", "InRepair", "Retired"];
Guid[] equipmentIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
int sent = 0;

async Task SendOne()
{
    int asset = sent % assetTags.Length;
    EquipmentStatusChanged message = new()
    {
        EquipmentId = equipmentIds[asset],
        AssetTag = assetTags[asset],
        Status = statuses[(sent / assetTags.Length) % statuses.Length],
        ChangedAtUtc = DateTime.UtcNow,
    };

    try
    {
        // Completes only once the broker has confirmed it. The second argument is the message-id; the outbox relay
        // passes the outbox row's id here, so a message sent twice keeps the same id and receivers can spot it.
        await publisher.PublishConfirmedAsync(message, messageId: null);
        sent++;
        Console.WriteLine($"Sent {message.AssetTag} → {message.Status}");
    }
    catch (BrokerUnavailableException ex)
    {
        // In the API, the outbox turns this into "try again on the next poll" without using up an attempt.
        Console.WriteLine($"Broker unavailable, not sent: {ex.Message}");
    }
}

int sendCount = args.Length == 2 && args[0] == "--send" && int.TryParse(args[1], out int n) ? n : 0;
if (sendCount > 0)
{
    for (int i = 0; i < sendCount; i++)
    {
        await SendOne();
    }
}
else
{
    Console.WriteLine("Press Enter to send an event, or q then Enter to quit.");
    while (Console.ReadLine() is { } line && !line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
    {
        await SendOne();
    }
}

await host.StopAsync();
