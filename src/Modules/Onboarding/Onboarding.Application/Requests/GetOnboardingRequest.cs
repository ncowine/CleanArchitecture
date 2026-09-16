using BuildingBlocks.Messaging;
using Onboarding.Application.Abstractions;

namespace Onboarding.Application.Requests;

/// <summary>The persisted onboarding request as stored — raw facts, no derived fields. See the separate
/// summary endpoint (GetOnboardingSummary) for readiness/blockers/cost computed from this same data.</summary>
public static class GetOnboardingRequest
{
    public sealed record Query(Guid OnboardingRequestId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string EmployeeName,
        DateOnly StartDate,
        string RequiredEquipmentCategory,
        string RequiredLicenceType,
        string RequiredAccessLevel,
        string Status,
        string? FailureReason,
        string EquipmentStepStatus,
        Guid? EquipmentId,
        string LicenceStepStatus,
        Guid? LicenceId,
        string AccessStepStatus,
        Guid? AccessId);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IOnboardingReadService _reads;

        public Handler(IOnboardingReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.OnboardingRequestId, cancellationToken);
    }
}
