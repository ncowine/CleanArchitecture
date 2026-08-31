using System.Text.Json.Serialization;

namespace BuildingBlocks.Auditing;

/// <summary>
/// What kind of activity an audit record describes. Lets one audit index hold writes, reads, and
/// third-party calls side by side while staying filterable (<c>category: Read</c> in Kibana), because
/// "who changed this row" and "who looked at this record" are different questions with different
/// retention and alerting rules.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditCategory
{
    /// <summary>A command that changed state. Carries the before/after entity changes.</summary>
    Write,

    /// <summary>A data-access request — someone read data. Carries no entity changes.</summary>
    Read,

    /// <summary>Data fetched from or pushed to a system we don't own (a third-party API, a file drop, a queue).</summary>
    External,

    /// <summary>An authentication/authorisation event — sign-in, token exchange, permission denied.</summary>
    Security,

    /// <summary>Anything else worth recording that the categories above don't describe.</summary>
    Custom,
}
