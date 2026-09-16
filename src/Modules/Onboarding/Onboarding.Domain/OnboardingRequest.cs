namespace Onboarding.Domain;

/// <summary>
/// A new hire's IT onboarding request: what they need (equipment category, licence type, access level)
/// and how far the provisioning saga has gotten. The three step fields are the stored facts a summary
/// view derives readiness/blockers/cost from later — this entity only records what happened, it doesn't
/// compute anything about it.
/// <para>
/// Two engines can run the same saga against this entity (see <c>ApproveOnboardingInstant</c> and
/// <c>ApproveOnboardingStandard</c> in Onboarding.Application) — both call the same Record*/*Compensated
/// methods, so the entity has no idea which engine is driving it.
/// </para>
/// </summary>
public sealed class OnboardingRequest
{
    public Guid Id { get; private set; }
    public string EmployeeName { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public string RequiredEquipmentCategory { get; private set; } = null!;
    public string RequiredLicenceType { get; private set; } = null!;
    public string RequiredAccessLevel { get; private set; } = null!;
    public OnboardingStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public StepStatus EquipmentStepStatus { get; private set; }
    public Guid? EquipmentId { get; private set; }

    public StepStatus LicenceStepStatus { get; private set; }
    public Guid? LicenceId { get; private set; }

    public StepStatus AccessStepStatus { get; private set; }
    public Guid? AccessId { get; private set; }

    private OnboardingRequest() { }

    private OnboardingRequest(
        Guid id, string employeeName, DateOnly startDate,
        string requiredEquipmentCategory, string requiredLicenceType, string requiredAccessLevel,
        DateTime createdOnUtc)
    {
        Id = id;
        EmployeeName = employeeName;
        StartDate = startDate;
        RequiredEquipmentCategory = requiredEquipmentCategory;
        RequiredLicenceType = requiredLicenceType;
        RequiredAccessLevel = requiredAccessLevel;
        Status = OnboardingStatus.Pending;
        CreatedOnUtc = createdOnUtc;
        EquipmentStepStatus = StepStatus.NotStarted;
        LicenceStepStatus = StepStatus.NotStarted;
        AccessStepStatus = StepStatus.NotStarted;
    }

    public static OnboardingRequest Create(
        string employeeName, DateOnly startDate,
        string requiredEquipmentCategory, string requiredLicenceType, string requiredAccessLevel)
    {
        if (string.IsNullOrWhiteSpace(employeeName))
            throw new DomainException("Employee name is required.");
        if (string.IsNullOrWhiteSpace(requiredEquipmentCategory))
            throw new DomainException("Required equipment category is required.");
        if (string.IsNullOrWhiteSpace(requiredLicenceType))
            throw new DomainException("Required licence type is required.");
        if (string.IsNullOrWhiteSpace(requiredAccessLevel))
            throw new DomainException("Required access level is required.");

        return new OnboardingRequest(
            Guid.NewGuid(), employeeName.Trim(), startDate,
            requiredEquipmentCategory.Trim(), requiredLicenceType.Trim(), requiredAccessLevel.Trim(),
            DateTime.UtcNow);
    }

    /// <summary>Pending → InProgress. Both engines call this once, at the start of the saga.</summary>
    public void Approve()
    {
        if (Status != OnboardingStatus.Pending)
            throw new DomainException($"Onboarding request '{Id}' has already been approved (status: {Status}).");

        Status = OnboardingStatus.InProgress;
    }

    public void RecordEquipmentReserved(Guid equipmentId)
    {
        EquipmentStepStatus = StepStatus.Completed;
        EquipmentId = equipmentId;
    }

    public void RecordEquipmentCompensated()
    {
        EquipmentStepStatus = StepStatus.Compensated;
    }

    public void RecordEquipmentFailed() => EquipmentStepStatus = StepStatus.Failed;

    public void RecordLicenceAllocated(Guid licenceId)
    {
        LicenceStepStatus = StepStatus.Completed;
        LicenceId = licenceId;
    }

    public void RecordLicenceCompensated()
    {
        LicenceStepStatus = StepStatus.Compensated;
    }

    public void RecordLicenceFailed() => LicenceStepStatus = StepStatus.Failed;

    public void RecordAccessProvisioned(Guid accessId)
    {
        AccessStepStatus = StepStatus.Completed;
        AccessId = accessId;
    }

    public void RecordAccessFailed() => AccessStepStatus = StepStatus.Failed;

    /// <summary>InProgress → Ready. Call once all three steps have completed.</summary>
    public void MarkReady()
    {
        if (Status != OnboardingStatus.InProgress)
            throw new DomainException($"Onboarding request '{Id}' cannot become ready from status {Status}.");

        Status = OnboardingStatus.Ready;
    }

    /// <summary>InProgress → Failed, after compensation has run for whichever steps had completed.</summary>
    public void MarkFailed(string reason)
    {
        if (Status != OnboardingStatus.InProgress)
            throw new DomainException($"Onboarding request '{Id}' cannot be marked failed from status {Status}.");

        Status = OnboardingStatus.Failed;
        FailureReason = reason;
    }
}
