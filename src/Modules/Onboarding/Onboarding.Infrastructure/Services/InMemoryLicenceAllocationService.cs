using Onboarding.Application.Abstractions;

namespace Onboarding.Infrastructure.Services;

/// <summary>
/// Stand-in for a real licence-management integration: a small fixed pool per known licence type, held
/// in memory (registered as a singleton — resets on restart, which is fine for a mock). Realistic enough
/// to actually run out, which is what lets the exercise demonstrate a licence-allocation failure without
/// needing a real integration. Idempotent on <c>onboardingRequestId</c>: tracks which request holds which
/// licence so a retried/redelivered call doesn't allocate twice.
/// </summary>
internal sealed class InMemoryLicenceAllocationService : ILicenceAllocationService
{
    private static readonly string[] KnownTypes = ["Standard", "Developer", "Premium"];
    private const int PoolSizePerType = 5;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _available =
        KnownTypes.ToDictionary(type => type, _ => PoolSizePerType, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, (Guid LicenceId, string LicenceType)> _allocations = new();

    public Task<LicenceAllocationResult> AllocateAsync(
        Guid onboardingRequestId, string licenceType, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_allocations.TryGetValue(onboardingRequestId, out var existing))
            {
                return Task.FromResult(new LicenceAllocationResult(true, existing.LicenceId, null));
            }

            if (!_available.TryGetValue(licenceType, out var remaining))
            {
                return Task.FromResult(new LicenceAllocationResult(false, null, $"Unknown licence type '{licenceType}'."));
            }

            if (remaining <= 0)
            {
                return Task.FromResult(new LicenceAllocationResult(false, null, $"No '{licenceType}' licences left in the pool."));
            }

            _available[licenceType] = remaining - 1;
            var licenceId = Guid.NewGuid();
            _allocations[onboardingRequestId] = (licenceId, licenceType);
            return Task.FromResult(new LicenceAllocationResult(true, licenceId, null));
        }
    }

    public Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_allocations.Remove(onboardingRequestId, out var allocation))
            {
                _available[allocation.LicenceType]++;
            }
        }

        return Task.CompletedTask;
    }
}
