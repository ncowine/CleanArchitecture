namespace Onboarding.Domain;

/// <summary>
/// Bookkeeping for the Standard (persisted/resumable) saga engine: which step it's on and whether it's
/// moving forward or unwinding. This is what makes the saga resumable — a crash leaves this row exactly
/// where the last committed outbox message left it, and the next poll of the outbox processor picks up
/// from here rather than from anything held in memory. One row per onboarding request.
/// </summary>
public sealed class OnboardingSagaState
{
    public Guid Id { get; private set; }
    public Guid OnboardingRequestId { get; private set; }
    public SagaStep CurrentStep { get; private set; }
    public SagaStatus Status { get; private set; }
    public DateTime StartedOnUtc { get; private set; }
    public DateTime? FinishedOnUtc { get; private set; }

    private OnboardingSagaState() { }

    private OnboardingSagaState(Guid id, Guid onboardingRequestId, DateTime startedOnUtc)
    {
        Id = id;
        OnboardingRequestId = onboardingRequestId;
        CurrentStep = SagaStep.ReserveEquipment;
        Status = SagaStatus.InProgress;
        StartedOnUtc = startedOnUtc;
    }

    public static OnboardingSagaState Start(Guid onboardingRequestId) =>
        new(Guid.NewGuid(), onboardingRequestId, DateTime.UtcNow);

    /// <summary>Moving forward: a step just succeeded and the next one has been enqueued.</summary>
    public void AdvanceTo(SagaStep step) => CurrentStep = step;

    /// <summary>A later step failed; unwinding begins from the given already-completed step.</summary>
    public void BeginCompensating(SagaStep fromStep)
    {
        Status = SagaStatus.Compensating;
        CurrentStep = fromStep;
    }

    public void Complete()
    {
        Status = SagaStatus.Completed;
        CurrentStep = SagaStep.Done;
        FinishedOnUtc = DateTime.UtcNow;
    }

    /// <summary>The first step failed — nothing had succeeded yet, so there is nothing to compensate.</summary>
    public void MarkFailed()
    {
        Status = SagaStatus.Failed;
        CurrentStep = SagaStep.Done;
        FinishedOnUtc = DateTime.UtcNow;
    }

    /// <summary>Unwinding has finished — every step that had succeeded has now been released.</summary>
    public void MarkCompensated()
    {
        Status = SagaStatus.Compensated;
        CurrentStep = SagaStep.Done;
        FinishedOnUtc = DateTime.UtcNow;
    }
}
