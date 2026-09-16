namespace Onboarding.Domain;

public enum SagaStatus
{
    InProgress,
    Compensating,
    Completed,

    /// <summary>Ended unsuccessfully with nothing to compensate — the very first step failed.</summary>
    Failed,

    /// <summary>Ended unsuccessfully after rolling back one or more steps that had already succeeded.</summary>
    Compensated,
}
