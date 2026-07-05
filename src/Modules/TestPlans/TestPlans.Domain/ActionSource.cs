namespace TestPlans.Domain;

/// <summary>
/// Where an <see cref="ActionLogEntry"/> originated: recorded natively against the primary system, or
/// synced in from the Tester Guide app via the outbox. Lets the primary tell its own history apart from
/// tool-driven activity.
/// </summary>
public enum ActionSource
{
    Primary,
    Guide,
}
