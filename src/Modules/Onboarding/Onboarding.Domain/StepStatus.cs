namespace Onboarding.Domain;

/// <summary>One shape shared by all three saga steps (equipment, licence, access).</summary>
public enum StepStatus
{
    NotStarted,

    /// <summary>The step succeeded and is still in effect.</summary>
    Completed,

    /// <summary>The step had succeeded, then was rolled back by a compensating action.</summary>
    Compensated,

    /// <summary>The step itself was the one that failed (never compensated — nothing to undo).</summary>
    Failed,
}
