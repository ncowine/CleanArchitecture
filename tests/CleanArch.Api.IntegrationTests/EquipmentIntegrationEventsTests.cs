using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.RealTime;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Infrastructure;
using Equipment.Infrastructure.Persistence;
using Equipment.Messages;
using Messaging.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Data;
using SharedKernel.DataService;
using Xunit;
using MessagingCorrelation = Messaging.Hosting.CorrelationContext;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// One Equipment feature, both channels (docs/messaging/adr/0003): the real module, mediator pipeline, SQLite
/// database and outbox processor, with the two transports replaced by recorders — SignalR by an
/// <see cref="IRealtimeNotifier"/>, RabbitMQ by an <see cref="IConfirmedMessagePublisher"/>.
/// </summary>
public sealed class EquipmentIntegrationEventsTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"equipment-events-{Guid.NewGuid():N}.db");
    private readonly string _referenceDbPath = Path.Combine(Path.GetTempPath(), $"reference-events-{Guid.NewGuid():N}.db");
    private readonly RecordingRealtimeNotifier _signalR = new();
    private readonly RecordingPublisher _rabbitMq = new();
    private ServiceProvider _withBroker = null!;
    private ServiceProvider _withoutBroker = null!;

    public async Task InitializeAsync()
    {
        _withBroker = await BuildAsync(withBroker: true);
        _withoutBroker = await BuildAsync(withBroker: false);
    }

    public async Task DisposeAsync()
    {
        await _withBroker.DisposeAsync();
        await _withoutBroker.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _referenceDbPath })
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task Creating_equipment_notifies_SignalR_at_commit_and_RabbitMQ_through_the_outbox()
    {
        Guid id;
        string? requestCorrelationId;
        using (var scope = _withBroker.CreateScope())
        {
            requestCorrelationId = scope.ServiceProvider.GetRequiredService<BuildingBlocks.Correlation.ICorrelationContext>().CorrelationId;
            id = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        }

        // SignalR: pushed as soon as the request's transaction committed.
        var (group, pushed) = Assert.Single(_signalR.Sent);
        Assert.Equal("equipment", group);
        Assert.Equal(nameof(EquipmentCreated), pushed.Type);
        Assert.Equal(id, Assert.IsType<EquipmentCreated>(pushed.Payload).Id);

        // RabbitMQ: nothing sent yet — the event is a committed row in the module's own database.
        Assert.Empty(_rabbitMq.Published);
        var row = await SingleOutboxRowAsync();
        Assert.Equal(nameof(EquipmentCreated), row.Type);
        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(requestCorrelationId, row.CorrelationId);

        // The outbox processor (polling every 2 s) relays it with a confirmed publish.
        await RunOutboxProcessorUntilAsync(() => _rabbitMq.Published.Count == 1);

        var (message, messageId, correlationId) = Assert.Single(_rabbitMq.Published);
        var created = Assert.IsType<EquipmentCreated>(message);
        Assert.Equal(id, created.Id);
        Assert.Equal("ThinkPad X1", created.Name);
        Assert.Equal("Laptop", created.Category);
        Assert.Equal("LAP-001", created.AssetTag);
        Assert.Equal("Available", created.Status);
        Assert.Equal(row.Id.ToString("N"), messageId);
        Assert.Equal(requestCorrelationId, correlationId);
        Assert.NotNull((await SingleOutboxRowAsync()).ProcessedOnUtc);
    }

    [Fact]
    public async Task Without_a_broker_SignalR_still_works_and_no_outbox_rows_are_written()
    {
        using (var scope = _withoutBroker.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("Dell U2723", EquipmentCategory.Monitor, "MON-001"), default);
        }

        Assert.Equal(nameof(EquipmentCreated), Assert.Single(_signalR.Sent).Event.Type);

        using var check = _withoutBroker.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<EquipmentDbContext>().Set<OutboxMessage>().ToListAsync());
    }

    private async Task<ServiceProvider> BuildAsync(bool withBroker)
    {
        // Each mode gets its own database file, so the two tests can't see each other's rows.
        var suffix = withBroker ? "" : "-nobroker";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddMediator();
        services.AddRealtimeDispatch();
        services.AddSingleton<IRealtimeNotifier>(_signalR);
        services.AddSharedKernelReferenceData($"Data Source={_referenceDbPath}");
        services.AddEquipmentModule($"Data Source={_dbPath}{suffix}");

        if (withBroker)
        {
            // What the host does when "Messaging" is configured — minus AddMessaging itself, whose confirmed
            // publisher is replaced by the recorder.
            services.AddSingleton<IConfirmedMessagePublisher>(_rabbitMq);
            services.AddEquipmentIntegrationEvents();
        }

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>().Database.MigrateAsync();
        return provider;
    }

    private async Task<OutboxMessage> SingleOutboxRowAsync()
    {
        using var scope = _withBroker.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>()
            .Set<OutboxMessage>().AsNoTracking().SingleAsync();
    }

    /// <summary>Runs only the outbox processor (not the cache pre-warmers) until the condition holds.</summary>
    private async Task RunOutboxProcessorUntilAsync(Func<bool> condition)
    {
        var processors = _withBroker.GetServices<IHostedService>()
            .Where(service => service.GetType().Name.StartsWith("OutboxProcessor", StringComparison.Ordinal))
            .ToList();
        Assert.Single(processors);

        foreach (var processor in processors)
        {
            await processor.StartAsync(CancellationToken.None);
        }

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The outbox processor didn't publish within 15 s.");
                await Task.Delay(100);
            }
        }
        finally
        {
            foreach (var processor in processors)
            {
                await processor.StopAsync(CancellationToken.None);
            }
        }
    }

    private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
    {
        public List<(string Group, RealtimeEvent Event)> Sent { get; } = [];

        public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken)
        {
            lock (Sent)
            {
                Sent.Add((group, realtimeEvent));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IConfirmedMessagePublisher
    {
        public List<(object Message, string? MessageId, string? CorrelationId)> Published { get; } = [];

        public Task PublishConfirmedAsync(object message, string? messageId, CancellationToken cancellationToken = default)
        {
            lock (Published)
            {
                Published.Add((message, messageId, MessagingCorrelation.Current));
            }

            return Task.CompletedTask;
        }
    }
}
