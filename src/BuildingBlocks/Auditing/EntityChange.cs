using System.Text.Json.Serialization;

namespace BuildingBlocks.Auditing;

/// <summary>
/// What kind of write happened to an entity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeOperation
{
    Added,
    Modified,
    Deleted,
}

/// <summary>
/// A single property that changed, with its before/after values (as strings so a single audit index
/// can hold changes from any entity without mapping conflicts). Sensitive values are redacted at capture.
/// </summary>
public sealed record PropertyChange(string Name, string? OldValue, string? NewValue);

/// <summary>
/// The change to one entity within a command: which entity (type + primary key), the operation, and the
/// per-property before/after values. This is what lets you see exactly what data was added or edited —
/// and forms the basis for a human-driven revert (issue a compensating command from this).
/// </summary>
public sealed record EntityChange(
    string EntityType,
    string EntityId,
    ChangeOperation Operation,
    IReadOnlyList<PropertyChange> Properties);
