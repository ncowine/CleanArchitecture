namespace TestPlans.Domain;

/// <summary>
/// The outcome of actioning a task for a given (platform, version/sub-version). <see cref="CheckedOut"/>
/// marks a task a tester has taken but not yet resolved; the rest are terminal outcomes.
/// </summary>
public enum TaskResultStatus
{
    CheckedOut,
    Pass,
    Fail,
    Skip,
}
