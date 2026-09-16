namespace Equipment.Contracts;

/// <summary>
/// The Equipment module's published reservation contract. The Onboarding module calls this directly
/// (constructor injection — Onboarding references this project) to reserve/release equipment for a new
/// hire without depending on Equipment's domain model: categories and results cross the boundary as
/// primitives. <paramref name="onboardingRequestId"/> doubles as the idempotency key — both methods are
/// safe to call more than once for the same request (a redelivered outbox message, a retried instant
/// approval).
/// </summary>
public interface IEquipmentReservationService
{
    Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="Reserved"/> false is an expected business outcome (nothing in stock), not an error —
/// callers branch on it rather than catching an exception. <paramref name="Reason"/> is set only when
/// <paramref name="Reserved"/> is false.
/// </summary>
public sealed record EquipmentReservationResult(bool Reserved, Guid? EquipmentId, string? Reason);
