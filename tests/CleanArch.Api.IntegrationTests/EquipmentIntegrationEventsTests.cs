using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Infrastructure;
using Equipment.Infrastructure.IntegrationEvents;
using Equipment.Infrastructure.Persistence;
using Equipment.Messages;
using Messaging;
using Messaging.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Data;
using SharedKernel.DataService;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// One Equipment feature, both channels: the real module, mediator pipeline and SQLite database, with the two
/// transports replaced by recorders — SignalR by an <see cref="IRealtimeNotifier"/>, RabbitMQ by an
/// <see cref="IMessagePublisher"/>.
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
    public async Task Creating_equipment_notifies_SignalR_and_RabbitMQ_after_the_commit()
    {
        Guid id;
        using (var scope = _withBroker.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        }

        var (group, pushed) = Assert.Single(_signalR.Sent);
        Assert.Equal("equipment", group);
        Assert.Equal(nameof(EquipmentCreated), pushed.Type);

        var created = Assert.IsType<EquipmentCreated>(Assert.Single(_rabbitMq.Published));
        Assert.Equal(id, created.Id);
        Assert.Equal("ThinkPad X1", created.Name);
        Assert.Equal("Laptop", created.Category);
        Assert.Equal("LAP-001", created.AssetTag);
        Assert.Equal("Available", created.Status);
    }

    [Fact]
    public async Task A_failed_commit_publishes_nothing()
    {
        using (var scope = _withBroker.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-DUP"), default);
        }

        // The handler queues its event, then the commit fails on the duplicate asset tag.
        using (var scope = _withBroker.CreateScope())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-DUP"), default));
        }

        Assert.Single(_rabbitMq.Published);
        Assert.Single(_signalR.Sent);
    }

    [Fact]
    public async Task Without_a_broker_SignalR_still_works_and_the_events_are_dropped()
    {
        using (var scope = _withoutBroker.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("Dell U2723", EquipmentCategory.Monitor, "MON-001"), default);
        }

        Assert.Equal(nameof(EquipmentCreated), Assert.Single(_signalR.Sent).Event.Type);
        Assert.Empty(_rabbitMq.Published);
    }

    [Fact]
    public async Task A_legacy_EquipmentDisposed_deletes_through_the_command_and_a_repeat_is_harmless()
    {
        Guid id;
        using (var scope = _withoutBroker.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new CreateEquipment.Command("ThinkPad T14", EquipmentCategory.Laptop, "LAP-002"), default);
        }

        var disposed = new EquipmentDisposed { EquipmentId = id, AssetTag = "LAP-002", DisposedBy = "net472 POC" };
        for (var delivery = 0; delivery < 2; delivery++)   // at-least-once: the same message twice
        {
            using var scope = _withoutBroker.CreateScope();
            var handler = new EquipmentDisposedHandler(
                scope.ServiceProvider.GetRequiredService<ISender>(), NullLogger<EquipmentDisposedHandler>.Instance);
            await handler.Handle(disposed, new MessageContext(
                "Poc.Legacy.Events.EquipmentDisposed", "Main", $"m{delivery}", delivery > 0,
                new Dictionary<string, string>(), "Poc.Net472", "Poc.Legacy.Events.EquipmentDisposed"), default);
        }

        using (var check = _withoutBroker.CreateScope())
        {
            Assert.Null(await check.ServiceProvider.GetRequiredService<EquipmentDbContext>()
                .Set<EquipmentAsset>().FindAsync(id));
        }

        // The same outgoing event as an HTTP delete, once.
        Assert.Equal(new[] { nameof(EquipmentCreated), nameof(EquipmentDeleted) }, _signalR.Sent.Select(s => s.Event.Type));
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
            // What AddMessaging provides, as a recorder.
            services.AddSingleton<IMessagePublisher>(_rabbitMq);
        }

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>().Database.MigrateAsync();
        return provider;
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

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public List<object> Published { get; } = [];

        public void Publish<TMessage>(TMessage message) => Published.Add(message!);

        public void Publish<TMessage>(TMessage message, PublishOptions options) => Published.Add(message!);
    }
}
