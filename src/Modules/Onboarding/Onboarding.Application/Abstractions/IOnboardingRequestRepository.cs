using Onboarding.Domain;

namespace Onboarding.Application.Abstractions;

public interface IOnboardingRequestRepository
{
    Task AddAsync(OnboardingRequest request, CancellationToken cancellationToken);

    Task<OnboardingRequest?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}
