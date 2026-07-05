namespace TesterGuide.Domain;

/// <summary>
/// The outcome a tester records for a task in a config. Mirrors the primary system's status values but is
/// owned independently by this module (no cross-module domain dependency); it is mapped to the primary by
/// name when an action syncs.
/// </summary>
public enum ActionStatus
{
    CheckedOut,
    Pass,
    Fail,
    Skip,
}
