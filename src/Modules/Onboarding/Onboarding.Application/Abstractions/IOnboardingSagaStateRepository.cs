using Onboarding.Domain;

namespace Onboarding.Application.Abstractions;

public interface IOnboardingSagaStateRepository
{
    Task AddAsync(OnboardingSagaState state, CancellationToken cancellationToken);

    Task<OnboardingSagaState?> GetByRequestIdAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}
