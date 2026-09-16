using Onboarding.Application.Requests;
using Xunit;

namespace CleanArch.UnitTests;

/// <summary>
/// Pure derivation logic — no fakes, no database. Builds a raw <see cref="GetOnboardingRequest.Response"/>
/// by hand for each scenario and checks what <see cref="GetOnboardingSummary.BuildResponse"/> computes
/// from it, since none of these fields are stored columns.
/// </summary>
public class GetOnboardingSummaryTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);

    private static GetOnboardingRequest.Response Raw(
        DateOnly startDate,
        string status = "Pending",
        string? failureReason = null,
        string equipmentStep = "NotStarted",
        string licenceStep = "NotStarted",
        string accessStep = "NotStarted",
        string equipmentCategory = "Laptop",
        string licenceType = "Standard") =>
        new(
            Guid.NewGuid(), "Jane Doe", startDate, equipmentCategory, licenceType, "Basic",
            status, failureReason, equipmentStep, null, licenceStep, null, accessStep, null);

    [Fact]
    public void Far_future_pending_request_is_in_progress_with_nothing_missing_yet_completed()
    {
        var raw = Raw(Today.AddDays(76));

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal("InProgress", summary.Status);
        Assert.Equal(0, summary.ReadinessPercentage);
        Assert.Equal(76, summary.DaysRemaining);
        Assert.Equal(["Equipment", "Licence", "Access"], summary.MissingRequirements);
        Assert.Empty(summary.CurrentBlockers);
    }

    [Fact]
    public void Pending_request_starting_within_the_risk_window_is_at_risk_even_without_a_failure()
    {
        var raw = Raw(Today.AddDays(1));

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal("AtRisk", summary.Status);
        Assert.Equal(1, summary.DaysRemaining);
        Assert.Empty(summary.CurrentBlockers); // at risk from time pressure alone, nothing has failed
    }

    [Fact]
    public void Ready_request_is_fully_ready_with_nothing_missing_or_blocking()
    {
        var raw = Raw(Today.AddDays(15), status: "Ready",
            equipmentStep: "Completed", licenceStep: "Completed", accessStep: "Completed");

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal("Ready", summary.Status);
        Assert.Equal(100, summary.ReadinessPercentage);
        Assert.Empty(summary.MissingRequirements);
        Assert.Empty(summary.CurrentBlockers);
    }

    [Fact]
    public void Failed_request_is_at_risk_with_the_failure_reason_as_a_blocker()
    {
        var raw = Raw(Today.AddDays(15), status: "Failed", failureReason: "Unknown access level 'GodMode'.",
            equipmentStep: "Compensated", licenceStep: "Compensated", accessStep: "Failed");

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal("AtRisk", summary.Status);
        Assert.Equal(0, summary.ReadinessPercentage); // nothing is actually in place after compensation
        Assert.Equal(["Equipment", "Licence", "Access"], summary.MissingRequirements);
        Assert.Equal(["Unknown access level 'GodMode'."], summary.CurrentBlockers);
    }

    [Fact]
    public void An_overdue_pending_request_gets_a_negative_days_remaining_and_an_overdue_blocker()
    {
        var raw = Raw(Today.AddDays(-3));

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal(-3, summary.DaysRemaining);
        Assert.Equal("AtRisk", summary.Status);
        Assert.Contains("3 day(s) ago", Assert.Single(summary.CurrentBlockers));
    }

    [Fact]
    public void Two_of_three_steps_completed_gives_partial_readiness()
    {
        var raw = Raw(Today.AddDays(15), equipmentStep: "Completed", licenceStep: "Completed");

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal(67, summary.ReadinessPercentage); // 2/3 rounded
        Assert.Equal(["Access"], summary.MissingRequirements);
    }

    [Theory]
    [InlineData("Laptop", "Standard", 1700)]
    [InlineData("Monitor", "Premium", 1200)]
    [InlineData("Phone", "Developer", 950)]
    [InlineData("UnknownCategory", "UnknownLicence", 700)] // falls back to defaults for both
    public void Estimated_cost_sums_equipment_and_licence_lookups(string category, string licenceType, decimal expected)
    {
        var raw = Raw(Today.AddDays(15), equipmentCategory: category, licenceType: licenceType);

        var summary = GetOnboardingSummary.BuildResponse(raw, Today);

        Assert.Equal(expected, summary.EstimatedCost);
    }
}
