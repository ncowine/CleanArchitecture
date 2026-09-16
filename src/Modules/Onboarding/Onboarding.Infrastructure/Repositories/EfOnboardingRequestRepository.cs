using Microsoft.EntityFrameworkCore;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;
using Onboarding.Infrastructure.Persistence;

namespace Onboarding.Infrastructure.Repositories;

internal sealed class EfOnboardingRequestRepository : IOnboardingRequestRepository
{
    private readonly OnboardingDbContext _db;

    public EfOnboardingRequestRepository(OnboardingDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(OnboardingRequest request, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Requests.AddAsync(request, cancellationToken);
    }

    public Task<OnboardingRequest?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
        _db.Requests.FirstOrDefaultAsync(request => request.Id == onboardingRequestId, cancellationToken);
}
