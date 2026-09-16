using Onboarding.Application.Requests;
using Onboarding.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class ApproveOnboardingInstantHandlerTests
{
    private static OnboardingRequest Pending() =>
        OnboardingRequest.Create("Jane Doe", new DateOnly(2026, 10, 1), "Laptop", "Standard", "Basic");

    private static (
        ApproveOnboardingInstant.Handler Handler,
        OnboardingRequest Request,
        FakeEquipmentReservationService Equipment,
        FakeLicenceAllocationService Licences,
        FakeAccessProvisioningService Access) Build(
        FakeEquipmentReservationService? equipment = null,
        FakeLicenceAllocationService? licences = null,
        FakeAccessProvisioningService? access = null)
    {
        var repository = new FakeOnboardingRequestRepository();
        var request = Pending();
        repository.Seed(request);
        equipment ??= FakeEquipmentReservationService.Succeeding();
        licences ??= FakeLicenceAllocationService.Succeeding();
        access ??= FakeAccessProvisioningService.Succeeding();
        var handler = new ApproveOnboardingInstant.Handler(repository, equipment, licences, access);
        return (handler, request, equipment, licences, access);
    }

    [Fact]
    public async Task All_steps_succeeding_marks_the_request_ready()
    {
        var (handler, request, equipment, licences, _) = Build();

        var result = await handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default);

        Assert.True(result.Ready);
        Assert.Null(result.FailureReason);
        Assert.Equal(OnboardingStatus.Ready, request.Status);
        Assert.Equal(StepStatus.Completed, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.Completed, request.LicenceStepStatus);
        Assert.Equal(StepStatus.Completed, request.AccessStepStatus);
        Assert.Empty(equipment.ReleaseCalls);
        Assert.Empty(licences.ReleaseCalls);
    }

    [Fact]
    public async Task Equipment_step_failing_fails_the_request_with_nothing_to_compensate()
    {
        var (handler, request, _, licences, access) = Build(
            equipment: FakeEquipmentReservationService.Failing("No Laptop in stock."));

        var result = await handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default);

        Assert.False(result.Ready);
        Assert.Equal("No Laptop in stock.", result.FailureReason);
        Assert.Equal(OnboardingStatus.Failed, request.Status);
        Assert.Equal(StepStatus.Failed, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.NotStarted, request.LicenceStepStatus); // never attempted
        Assert.Empty(licences.AllocateCalls);
        Assert.Empty(access.ProvisionCalls);
    }

    [Fact]
    public async Task Licence_step_failing_compensates_the_reserved_equipment()
    {
        var (handler, request, equipment, _, access) = Build(
            licences: FakeLicenceAllocationService.Failing("No Standard licences left."));

        var result = await handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default);

        Assert.False(result.Ready);
        Assert.Equal(OnboardingStatus.Failed, request.Status);
        Assert.Equal(StepStatus.Compensated, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.Failed, request.LicenceStepStatus);
        Assert.Equal(request.Id, Assert.Single(equipment.ReleaseCalls));
        Assert.Empty(access.ProvisionCalls); // never reached
    }

    [Fact]
    public async Task Access_step_failing_compensates_both_licence_and_equipment_in_reverse_order()
    {
        var (handler, request, equipment, licences, _) = Build(
            access: FakeAccessProvisioningService.Failing("Unknown access level 'GodMode'."));

        var result = await handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default);

        Assert.False(result.Ready);
        Assert.Equal("Unknown access level 'GodMode'.", result.FailureReason);
        Assert.Equal(OnboardingStatus.Failed, request.Status);
        Assert.Equal(StepStatus.Compensated, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.Compensated, request.LicenceStepStatus);
        Assert.Equal(StepStatus.Failed, request.AccessStepStatus);
        Assert.Equal(request.Id, Assert.Single(licences.ReleaseCalls));
        Assert.Equal(request.Id, Assert.Single(equipment.ReleaseCalls));
    }

    [Fact]
    public async Task Unknown_request_throws()
    {
        var (handler, _, _, _, _) = Build();

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ApproveOnboardingInstant.Command(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Approving_an_already_decided_request_throws()
    {
        var (handler, request, _, _, _) = Build();
        request.Approve();
        request.MarkReady();

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default));
    }
}
