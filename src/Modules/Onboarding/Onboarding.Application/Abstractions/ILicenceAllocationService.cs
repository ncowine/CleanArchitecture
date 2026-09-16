namespace Onboarding.Application.Abstractions;

/// <summary>
/// A simple service abstraction standing in for a real licence-management integration — not a separate
/// module, per the exercise. Idempotent on <paramref name="onboardingRequestId"/>: a redelivered/retried
/// call for the same request returns the allocation already made rather than allocating twice.
/// </summary>
public interface ILicenceAllocationService
{
    Task<LicenceAllocationResult> AllocateAsync(
        Guid onboardingRequestId, string licenceType, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="Allocated"/> false is an expected business outcome (unknown type, pool exhausted), not
/// an error — callers branch on it rather than catching an exception.
/// </summary>
public sealed record LicenceAllocationResult(bool Allocated, Guid? LicenceId, string? Reason);
