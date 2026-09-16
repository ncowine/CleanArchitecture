using BuildingBlocks.Messaging;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;

namespace Onboarding.Application.Requests;

/// <summary>
/// Business-facing view of an onboarding request: readiness, risk status, and cost, all computed from
/// the same rows <see cref="GetOnboardingRequest"/> returns raw — nothing here is a stored column. Reuses
/// the existing read (<see cref="IOnboardingReadService"/>) rather than a second database query; the
/// derivation itself (<see cref="BuildResponse"/>) is pure and takes "today" as a parameter so it can be
/// unit-tested without a clock or a database.
/// </summary>
public static class GetOnboardingSummary
{
    private const int AtRiskThresholdDays = 2;
    private const decimal DefaultEquipmentCost = 500m;
    private const decimal DefaultLicenceCost = 200m;

    // Simple constants, not a pricing module — per-category/per-type estimates, not real catalogue prices.
    private static readonly Dictionary<string, decimal> EquipmentCostByCategory =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Laptop"] = 1500m,
            ["Monitor"] = 400m,
            ["Phone"] = 450m,
            ["Other"] = 150m,
        };

    private static readonly Dictionary<string, decimal> LicenceCostByType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = 200m,
            ["Developer"] = 500m,
            ["Premium"] = 800m,
        };

    public sealed record Query(Guid OnboardingRequestId) : IRequest<Response?>, IAuditableRead
    {
        // Names whose data was read, so the record answers "whose?" and not just "which query?"
        public string AuditResource => $"OnboardingRequest/{OnboardingRequestId}";
    }

    public sealed record Response(
        Guid Id,
        string EmployeeName,
        DateOnly StartDate,
        string Status,
        int ReadinessPercentage,
        int DaysRemaining,
        IReadOnlyList<string> MissingRequirements,
        IReadOnlyList<string> CurrentBlockers,
        decimal EstimatedCost);

    /// <summary>
    /// Derives the summary from the raw stored request. "Missing requirements" is the neutral checklist
    /// of what hasn't happened yet, whether or not anything has gone wrong; "current blockers" is the set
    /// of concrete problems (a failure reason, a start date already passed) — the two answer different
    /// questions and a request can have items in one without the other.
    /// </summary>
    public static Response BuildResponse(GetOnboardingRequest.Response request, DateOnly today)
    {
        var daysRemaining = request.StartDate.DayNumber - today.DayNumber;

        var stepStatuses = new[] { request.EquipmentStepStatus, request.LicenceStepStatus, request.AccessStepStatus };
        var completedCount = stepStatuses.Count(status => status == nameof(StepStatus.Completed));
        var readiness = (int)Math.Round(completedCount / 3.0 * 100, MidpointRounding.AwayFromZero);

        var missing = new List<string>();
        if (request.EquipmentStepStatus != nameof(StepStatus.Completed)) missing.Add("Equipment");
        if (request.LicenceStepStatus != nameof(StepStatus.Completed)) missing.Add("Licence");
        if (request.AccessStepStatus != nameof(StepStatus.Completed)) missing.Add("Access");

        var isReady = request.Status == nameof(OnboardingStatus.Ready);

        var blockers = new List<string>();
        if (!string.IsNullOrEmpty(request.FailureReason))
        {
            blockers.Add(request.FailureReason);
        }
        if (daysRemaining < 0 && !isReady)
        {
            blockers.Add($"Start date was {-daysRemaining} day(s) ago and onboarding is not ready.");
        }

        var status =
            isReady ? "Ready"
            : request.Status == nameof(OnboardingStatus.Failed) ? "AtRisk"
            : daysRemaining <= AtRiskThresholdDays ? "AtRisk"
            : "InProgress";

        var estimatedCost =
            (EquipmentCostByCategory.TryGetValue(request.RequiredEquipmentCategory, out var equipmentCost) ? equipmentCost : DefaultEquipmentCost) +
            (LicenceCostByType.TryGetValue(request.RequiredLicenceType, out var licenceCost) ? licenceCost : DefaultLicenceCost);

        return new Response(
            request.Id, request.EmployeeName, request.StartDate, status, readiness, daysRemaining,
            missing, blockers, estimatedCost);
    }

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IOnboardingReadService _reads;

        public Handler(IOnboardingReadService reads)
        {
            _reads = reads;
        }

        public async Task<Response?> Handle(Query query, CancellationToken cancellationToken)
        {
            var request = await _reads.GetAsync(query.OnboardingRequestId, cancellationToken);
            return request is null ? null : BuildResponse(request, DateOnly.FromDateTime(DateTime.UtcNow));
        }
    }
}
