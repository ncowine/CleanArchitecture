using Microsoft.EntityFrameworkCore;
using Onboarding.Application.Abstractions;
using Onboarding.Application.Requests;
using Onboarding.Infrastructure.Persistence;

namespace Onboarding.Infrastructure.Reads;

internal sealed class OnboardingReadService : IOnboardingReadService
{
    private readonly OnboardingDbContext _db;

    public OnboardingReadService(OnboardingDbContext db)
    {
        _db = db;
    }

    public Task<GetOnboardingRequest.Response?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
        _db.Requests
            .AsNoTracking()
            .Where(request => request.Id == onboardingRequestId)
            .Select(request => new GetOnboardingRequest.Response(
                request.Id,
                request.EmployeeName,
                request.StartDate,
                request.RequiredEquipmentCategory,
                request.RequiredLicenceType,
                request.RequiredAccessLevel,
                request.Status.ToString(),
                request.FailureReason,
                request.EquipmentStepStatus.ToString(),
                request.EquipmentId,
                request.LicenceStepStatus.ToString(),
                request.LicenceId,
                request.AccessStepStatus.ToString(),
                request.AccessId))
            .FirstOrDefaultAsync(cancellationToken);
}
