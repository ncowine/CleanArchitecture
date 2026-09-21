using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.RealTime;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Infrastructure;
using Equipment.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onboarding.Application.Requests;
using Onboarding.Infrastructure;
using Onboarding.Infrastructure.Persistence;
using SharedKernel.Data;
using SharedKernel.DataService;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// Both saga engines against real production wiring: real EquipmentDbContext and OnboardingDbContext
/// (temp-file SQLite), the real cross-module call from Onboarding into Equipment's published
/// <c>IEquipmentReservationService</c>, and — for the Standard engine — the real
/// <see cref="OnboardingOutboxDispatcher"/>. The only thing not real is time: rather than waiting on the
/// background <c>OutboxProcessor</c>'s 2-second poll (which the live smoke-testing already exercised end
/// to end, restart and all), <see cref="DrainOutboxAsync"/> drives the same dispatcher deterministically
/// so the test suite stays fast and reproducible.
/// </summary>
public sealed class OnboardingSagaTests : IAsyncLifetime
{
    private readonly string _equipmentDbPath = Path.Combine(Path.GetTempPath(), $"equipment-saga-{Guid.NewGuid():N}.db");
    private readonly string _onboardingDbPath = Path.Combine(Path.GetTempPath(), $"onboarding-saga-{Guid.NewGuid():N}.db");
    private readonly string _referenceDbPath = Path.Combine(Path.GetTempPath(), $"reference-saga-{Guid.NewGuid():N}.db");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddMediator();
        services.AddRealtimeDispatch();
        services.AddSharedKernelReferenceData($"Data Source={_referenceDbPath}");
        services.AddEquipmentModule($"Data Source={_equipmentDbPath}");
        services.AddOnboardingModule($"Data Source={_onboardingDbPath}");

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<OnboardingDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();

        // SQLite's ADO.NET provider pools connections at the process level — disposing the DI container
        // doesn't close them, so the temp files stay locked until the pool is cleared.
        SqliteConnection.ClearAllPools();
        TryDeleteDatabaseFiles(_equipmentDbPath);
        TryDeleteDatabaseFiles(_onboardingDbPath);
        TryDeleteDatabaseFiles(_referenceDbPath);
    }

    [Fact]
    public async Task Instant_saga_succeeding_reserves_real_equipment_and_marks_the_request_ready()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var equipmentId = await sender.Send(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        var requestId = await sender.Send(
            new CreateOnboardingRequest.Command(
                "Jane Doe", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Laptop", "Standard", "Basic"),
            default);

        var result = await sender.Send(new ApproveOnboardingInstant.Command(requestId), default);

        Assert.True(result.Ready);
        var request = await sender.Send(new GetOnboardingRequest.Query(requestId), default);
        Assert.Equal("Ready", request!.Status);
        Assert.Equal(equipmentId, request.EquipmentId);

        var equipment = await sender.Send(new GetEquipment.Query(equipmentId), default);
        Assert.Equal("Reserved", equipment!.Status);
    }

    [Fact]
    public async Task Instant_saga_failing_on_access_releases_the_real_equipment_reservation()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var equipmentId = await sender.Send(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        var requestId = await sender.Send(
            new CreateOnboardingRequest.Command(
                "John Smith", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Laptop", "Standard", "GodMode"),
            default);

        var result = await sender.Send(new ApproveOnboardingInstant.Command(requestId), default);

        Assert.False(result.Ready);
        var request = await sender.Send(new GetOnboardingRequest.Query(requestId), default);
        Assert.Equal("Failed", request!.Status);

        // The proof that matters: the OTHER module's data was really released, not just this module's flag.
        var equipment = await sender.Send(new GetEquipment.Query(equipmentId), default);
        Assert.Equal("Available", equipment!.Status);
    }

    [Fact]
    public async Task Standard_saga_succeeding_converges_to_ready_after_draining_the_outbox()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var equipmentId = await sender.Send(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        var requestId = await sender.Send(
            new CreateOnboardingRequest.Command(
                "Alice Standard", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Laptop", "Developer", "Admin"),
            default);

        await sender.Send(new ApproveOnboardingStandard.Command(requestId), default);
        await DrainOutboxAsync(_provider);

        var request = await sender.Send(new GetOnboardingRequest.Query(requestId), default);
        Assert.Equal("Ready", request!.Status);
        Assert.Equal("Completed", request.EquipmentStepStatus);
        Assert.Equal("Completed", request.LicenceStepStatus);
        Assert.Equal("Completed", request.AccessStepStatus);

        var equipment = await sender.Send(new GetEquipment.Query(equipmentId), default);
        Assert.Equal("Reserved", equipment!.Status);
    }

    [Fact]
    public async Task Standard_saga_failing_on_access_compensates_licence_and_equipment_after_draining_the_outbox()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var equipmentId = await sender.Send(
            new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);
        var requestId = await sender.Send(
            new CreateOnboardingRequest.Command(
                "Carol Fails", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Laptop", "Standard", "RootAccess"),
            default);

        await sender.Send(new ApproveOnboardingStandard.Command(requestId), default);
        await DrainOutboxAsync(_provider);

        var request = await sender.Send(new GetOnboardingRequest.Query(requestId), default);
        Assert.Equal("Failed", request!.Status);
        Assert.Equal("Compensated", request.EquipmentStepStatus);
        Assert.Equal("Compensated", request.LicenceStepStatus);
        Assert.Equal("Failed", request.AccessStepStatus);

        var equipment = await sender.Send(new GetEquipment.Query(equipmentId), default);
        Assert.Equal("Available", equipment!.Status);
    }

    [Fact]
    public async Task Standard_saga_with_no_equipment_in_stock_fails_immediately_with_nothing_to_compensate()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        // No equipment created at all.
        var requestId = await sender.Send(
            new CreateOnboardingRequest.Command(
                "Dave NoStock", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Laptop", "Standard", "Basic"),
            default);

        await sender.Send(new ApproveOnboardingStandard.Command(requestId), default);
        await DrainOutboxAsync(_provider);

        var request = await sender.Send(new GetOnboardingRequest.Query(requestId), default);
        Assert.Equal("Failed", request!.Status);
        Assert.Equal("No available Laptop in stock.", request.FailureReason);
        Assert.Equal("Failed", request.EquipmentStepStatus); // nothing to compensate — the first step itself failed
    }

    /// <summary>
    /// Stands in for the background <c>OutboxProcessor</c>'s poll loop: repeatedly hands every pending
    /// message to the real dispatcher, exactly as the hosted service would, until none remain. A fresh
    /// scope per round mirrors the processor's own per-tick scope.
    /// </summary>
    private static async Task DrainOutboxAsync(IServiceProvider services, int maxRounds = 10)
    {
        for (var round = 0; round < maxRounds; round++)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher<OnboardingDbContext>>();

            var pending = await db.Outbox
                .Where(message => message.ProcessedOnUtc == null && message.DeadLetteredOnUtc == null)
                .OrderBy(message => message.OccurredOnUtc)
                .ToListAsync();

            if (pending.Count == 0)
            {
                return;
            }

            foreach (var message in pending)
            {
                await dispatcher.DispatchAsync(message.Id, message.Type, message.Content, default);
                message.ProcessedOnUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        throw new TimeoutException($"Outbox did not drain within {maxRounds} rounds — possible infinite loop.");
    }

    private static void TryDeleteDatabaseFiles(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-shm", dbPath + "-wal" })
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }
}
