using System.Text.Json.Serialization;

namespace BuildingBlocks.Auditing;

/// <summary>
/// How an audited operation ended. Three states, not two: a request the caller abandoned
/// (<see cref="Cancelled"/>) is neither a success nor a failure of the system, and collapsing it into
/// <see cref="Failed"/> makes "what actually broke?" unanswerable from the trail alone.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditOutcome
{
    /// <summary>Ran to completion.</summary>
    Succeeded,

    /// <summary>Threw. See the record's <c>Error</c> for why.</summary>
    Failed,

    /// <summary>The caller went away (request aborted, host shutting down) before it finished — not a
    /// system failure, but still worth a record of who was doing what when they gave up.</summary>
    Cancelled,
}
