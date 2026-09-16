using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Equipment.Application.Inventory;
using Equipment.Contracts;
using Equipment.Domain;
using Equipment.Infrastructure;
using Equipment.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// Exercises the Equipment module's real production wiring — <see cref="EquipmentDbContext"/> against an
/// actual (temp-file) SQLite database, the real <c>HybridCache</c>, and the real mediator pipeline
/// (transaction + realtime dispatch) — through the same <c>AddEquipmentModule</c> extension the host uses.
/// The only substitution is <see cref="IRealtimeNotifier"/>: a recording fake stands in for SignalR, the
/// one piece that would otherwise need a live socket.
/// </summary>
public sealed class EquipmentModuleTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"equipment-tests-{Guid.NewGuid():N}.db");
    private ServiceProvider _provider = null!;
    private RecordingRealtimeNotifier _notifier = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddMediator();
        services.AddRealtimeDispatch();
        services.AddSingleton<IRealtimeNotifier, RecordingRealtimeNotifier>();
        services.AddEquipmentModule($"Data Source={_dbPath}");

        _provider = services.BuildServiceProvider();
        _notifier = (RecordingRealtimeNotifier)_provider.GetRequiredService<IRealtimeNotifier>();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();

        // SQLite's ADO.NET provider pools connections at the process level — disposing the DI container
        // doesn't close them, so the temp file stays locked until the pool is cleared.
        SqliteConnection.ClearAllPools();
        TryDeleteDatabaseFiles(_dbPath);
    }

    [Fact]
    public async Task Creating_equipment_persists_it_and_publishes_a_realtime_event()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var id = await sender.Send(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

        var stored = await sender.Send(new GetEquipment.Query(id), default);
        Assert.NotNull(stored);
        Assert.Equal("ThinkPad X1", stored!.Name);
        Assert.Equal("Available", stored.Status);

        var (group, evt) = Assert.Single(_notifier.Sent);
        Assert.Equal("equipment", group);
        Assert.Equal("EquipmentCreated", evt.Type);
    }

    [Fact]
    public async Task A_second_read_is_served_from_cache_not_the_database()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var id = await sender.Send(
            new CreateEquipment.Command("Dell Monitor", EquipmentCategory.Monitor, "MON-001"), default);

        var first = await sender.Send(new GetEquipment.Query(id), default);
        Assert.Equal("Dell Monitor", first!.Name);

        // Change the row directly at the database level — bypassing the cache invalidator entirely, which
        // only a raw SQL statement (not the domain, which has no "just rename it" backdoor) can do.
        var db = scope.ServiceProvider.GetRequiredService<EquipmentDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE EquipmentAssets SET Name = 'Mutated Directly In The Database' WHERE Id = {id}");

        var second = await sender.Send(new GetEquipment.Query(id), default);

        // Still the ORIGINAL name — proof the second read came from the cache, not a fresh query.
        Assert.Equal("Dell Monitor", second!.Name);
    }

    [Fact]
    public async Task Updating_equipment_invalidates_the_cache_so_the_next_read_sees_the_change()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var id = await sender.Send(
            new CreateEquipment.Command("Dell Monitor", EquipmentCategory.Monitor, "MON-001"), default);
        await sender.Send(new GetEquipment.Query(id), default); // populate the cache

        var updated = await sender.Send(
            new UpdateEquipment.Command(id, "Dell Monitor 4K", EquipmentCategory.Monitor, "MON-001"), default);
        var afterUpdate = await sender.Send(new GetEquipment.Query(id), default);

        Assert.True(updated);
        Assert.Equal("Dell Monitor 4K", afterUpdate!.Name);
    }

    [Fact]
    public async Task Deleting_equipment_removes_it_and_a_later_read_is_a_miss()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var id = await sender.Send(
            new CreateEquipment.Command("Spare Phone", EquipmentCategory.Phone, "PHN-001"), default);
        await sender.Send(new GetEquipment.Query(id), default); // populate the cache

        var deleted = await sender.Send(new DeleteEquipment.Command(id), default);
        var afterDelete = await sender.Send(new GetEquipment.Query(id), default);

        Assert.True(deleted);
        Assert.Null(afterDelete); // cache was invalidated too, not just the row deleted
    }

    [Fact]
    public async Task Reserving_equipment_twice_for_the_same_onboarding_request_is_idempotent()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

        var reservations = scope.ServiceProvider.GetRequiredService<IEquipmentReservationService>();
        var onboardingRequestId = Guid.NewGuid();

        var first = await reservations.ReserveAsync(onboardingRequestId, "Laptop", default);
        var second = await reservations.ReserveAsync(onboardingRequestId, "Laptop", default);

        Assert.True(first.Reserved);
        Assert.True(second.Reserved);
        Assert.Equal(first.EquipmentId, second.EquipmentId); // the same reservation, not a second one
    }

    [Fact]
    public async Task The_catalogue_endpoint_reads_the_bundled_file_not_the_database()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var catalogue = await sender.Send(new GetEquipmentCatalogue.Query(), default);

        Assert.NotEmpty(catalogue);
        Assert.Contains(catalogue, item => item.Category == "Laptop");
    }

    private static void TryDeleteDatabaseFiles(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-shm", dbPath + "-wal" })
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
    {
        public List<(string Group, RealtimeEvent Event)> Sent { get; } = new();

        public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken)
        {
            Sent.Add((group, realtimeEvent));
            return Task.CompletedTask;
        }
    }
}
