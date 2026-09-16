using Onboarding.Application.Abstractions;

namespace Onboarding.Infrastructure.Services;

/// <summary>
/// Stand-in for a real access/identity-provisioning integration: accepts only a fixed set of known
/// access levels, held in memory (registered as a singleton). Requesting an unsupported level is how the
/// exercise demonstrates an access-provisioning failure and the compensation it triggers (release the
/// licence, release the equipment) without needing a real integration. Idempotent on
/// <c>onboardingRequestId</c>.
/// </summary>
internal sealed class InMemoryAccessProvisioningService : IAccessProvisioningService
{
    private static readonly string[] KnownLevels = ["Basic", "Standard", "Admin"];

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Guid> _provisioned = new();

    public Task<AccessProvisioningResult> ProvisionAsync(
        Guid onboardingRequestId, string accessLevel, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_provisioned.TryGetValue(onboardingRequestId, out var existingId))
            {
                return Task.FromResult(new AccessProvisioningResult(true, existingId, null));
            }

            if (!KnownLevels.Contains(accessLevel, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(new AccessProvisioningResult(false, null, $"Unknown access level '{accessLevel}'."));
            }

            var accessId = Guid.NewGuid();
            _provisioned[onboardingRequestId] = accessId;
            return Task.FromResult(new AccessProvisioningResult(true, accessId, null));
        }
    }

    public Task RevokeAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _provisioned.Remove(onboardingRequestId);
        }

        return Task.CompletedTask;
    }
}
