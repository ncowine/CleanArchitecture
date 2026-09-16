using Onboarding.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class OnboardingRequestTests
{
    private static readonly DateOnly StartDate = new(2026, 10, 1);

    private static OnboardingRequest Create() =>
        OnboardingRequest.Create(" Jane Doe ", StartDate, " Laptop ", " Standard ", " Basic ");

    [Fact]
    public void Create_trims_input_and_starts_pending_with_no_steps_started()
    {
        var request = Create();

        Assert.Equal("Jane Doe", request.EmployeeName);
        Assert.Equal("Laptop", request.RequiredEquipmentCategory);
        Assert.Equal("Standard", request.RequiredLicenceType);
        Assert.Equal("Basic", request.RequiredAccessLevel);
        Assert.Equal(OnboardingStatus.Pending, request.Status);
        Assert.Equal(StepStatus.NotStarted, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.NotStarted, request.LicenceStepStatus);
        Assert.Equal(StepStatus.NotStarted, request.AccessStepStatus);
        Assert.Null(request.FailureReason);
    }

    [Theory]
    [InlineData("", "Laptop", "Standard", "Basic")]
    [InlineData("Name", "", "Standard", "Basic")]
    [InlineData("Name", "Laptop", "", "Basic")]
    [InlineData("Name", "Laptop", "Standard", "")]
    public void Create_with_invalid_input_throws(string name, string equipment, string licence, string access) =>
        Assert.Throws<DomainException>(() => OnboardingRequest.Create(name, StartDate, equipment, licence, access));

    [Fact]
    public void Approve_moves_pending_to_in_progress()
    {
        var request = Create();

        request.Approve();

        Assert.Equal(OnboardingStatus.InProgress, request.Status);
    }

    [Fact]
    public void Approving_twice_throws()
    {
        var request = Create();
        request.Approve();

        Assert.Throws<DomainException>(request.Approve);
    }

    [Fact]
    public void Recording_each_step_updates_its_status_and_reference_id()
    {
        var request = Create();
        request.Approve();
        var equipmentId = Guid.NewGuid();
        var licenceId = Guid.NewGuid();
        var accessId = Guid.NewGuid();

        request.RecordEquipmentReserved(equipmentId);
        request.RecordLicenceAllocated(licenceId);
        request.RecordAccessProvisioned(accessId);

        Assert.Equal(StepStatus.Completed, request.EquipmentStepStatus);
        Assert.Equal(equipmentId, request.EquipmentId);
        Assert.Equal(StepStatus.Completed, request.LicenceStepStatus);
        Assert.Equal(licenceId, request.LicenceId);
        Assert.Equal(StepStatus.Completed, request.AccessStepStatus);
        Assert.Equal(accessId, request.AccessId);
    }

    [Fact]
    public void MarkReady_requires_in_progress_status()
    {
        var request = Create(); // still Pending

        Assert.Throws<DomainException>(request.MarkReady);
    }

    [Fact]
    public void MarkReady_from_in_progress_succeeds()
    {
        var request = Create();
        request.Approve();

        request.MarkReady();

        Assert.Equal(OnboardingStatus.Ready, request.Status);
    }

    [Fact]
    public void MarkFailed_records_the_reason()
    {
        var request = Create();
        request.Approve();

        request.MarkFailed("No laptops in stock.");

        Assert.Equal(OnboardingStatus.Failed, request.Status);
        Assert.Equal("No laptops in stock.", request.FailureReason);
    }

    [Fact]
    public void Compensation_methods_flip_completed_steps_back()
    {
        var request = Create();
        request.Approve();
        request.RecordEquipmentReserved(Guid.NewGuid());
        request.RecordLicenceAllocated(Guid.NewGuid());

        request.RecordLicenceCompensated();
        request.RecordEquipmentCompensated();

        Assert.Equal(StepStatus.Compensated, request.EquipmentStepStatus);
        Assert.Equal(StepStatus.Compensated, request.LicenceStepStatus);
    }
}
