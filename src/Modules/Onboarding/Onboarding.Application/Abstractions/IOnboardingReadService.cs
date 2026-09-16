using Onboarding.Application.Requests;

namespace Onboarding.Application.Abstractions;

public interface IOnboardingReadService
{
    Task<GetOnboardingRequest.Response?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}
