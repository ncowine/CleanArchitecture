namespace Onboarding.Application.Abstractions;

/// <summary>
/// A simple service abstraction standing in for a real access/identity-provisioning integration — not a
/// separate module, per the exercise. Idempotent on <paramref name="onboardingRequestId"/>.
/// </summary>
public interface IAccessProvisioningService
{
    Task<AccessProvisioningResult> ProvisionAsync(
        Guid onboardingRequestId, string accessLevel, CancellationToken cancellationToken);

    Task RevokeAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="Provisioned"/> false is an expected business outcome (unsupported level), not an error
/// — callers branch on it rather than catching an exception.
/// </summary>
public sealed record AccessProvisioningResult(bool Provisioned, Guid? AccessId, string? Reason);
