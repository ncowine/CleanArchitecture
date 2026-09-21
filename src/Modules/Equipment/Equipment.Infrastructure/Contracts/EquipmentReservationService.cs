using Equipment.Application.Abstractions;
using Equipment.Contracts;
using Equipment.Domain;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Equipment.Infrastructure.Contracts;

/// <summary>
/// Implements the Equipment module's published <see cref="IEquipmentReservationService"/> against the
/// Equipment DB. Called directly (constructor injection) by the Onboarding module's saga steps —
/// idempotent on <c>onboardingRequestId</c>, since either engine may call a step more than once (a
/// retried instant approval, a redelivered outbox message).
/// </summary>
internal sealed class EquipmentReservationService : IEquipmentReservationService
{
    private readonly EquipmentDbContext _db;
    private readonly IEquipmentChangeNotifier _changeNotifier;

    public EquipmentReservationService(EquipmentDbContext db, IEquipmentChangeNotifier changeNotifier)
    {
        _db = db;
        _changeNotifier = changeNotifier;
    }

    public async Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken)
    {
        // Idempotent: a redelivery/retry for the same request finds its own reservation already made.
        var already = await _db.Equipment
            .FirstOrDefaultAsync(asset => asset.ReservedForOnboardingRequestId == onboardingRequestId, cancellationToken);
        if (already is not null)
        {
            return new EquipmentReservationResult(true, already.Id, null);
        }

        if (!Enum.TryParse<EquipmentCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            return new EquipmentReservationResult(false, null, $"Unknown equipment category '{category}'.");
        }

        var candidate = await _db.Equipment
            .Where(asset => asset.Category == parsedCategory && asset.Status == EquipmentStatus.Available)
            .OrderBy(asset => asset.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            // Expected business outcome, not an error — callers branch on Reserved rather than catching.
            return new EquipmentReservationResult(false, null, $"No available {category} in stock.");
        }

        candidate.Reserve(onboardingRequestId);
        await _db.SaveChangesAsync(cancellationToken);
        _changeNotifier.Notify(candidate.Id);

        return new EquipmentReservationResult(true, candidate.Id, null);
    }

    public async Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var reserved = await _db.Equipment
            .FirstOrDefaultAsync(asset => asset.ReservedForOnboardingRequestId == onboardingRequestId, cancellationToken);
        if (reserved is null)
        {
            return; // idempotent — already released, or never reserved
        }

        reserved.Release();
        await _db.SaveChangesAsync(cancellationToken);
        _changeNotifier.Notify(reserved.Id);
    }
}
