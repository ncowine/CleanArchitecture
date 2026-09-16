using Microsoft.EntityFrameworkCore;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;
using Onboarding.Infrastructure.Persistence;

namespace Onboarding.Infrastructure.Repositories;

internal sealed class EfOnboardingSagaStateRepository : IOnboardingSagaStateRepository
{
    private readonly OnboardingDbContext _db;

    public EfOnboardingSagaStateRepository(OnboardingDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(OnboardingSagaState state, CancellationToken cancellationToken)
    {
        await _db.SagaStates.AddAsync(state, cancellationToken);
    }

    public Task<OnboardingSagaState?> GetByRequestIdAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
        _db.SagaStates.FirstOrDefaultAsync(state => state.OnboardingRequestId == onboardingRequestId, cancellationToken);
}
