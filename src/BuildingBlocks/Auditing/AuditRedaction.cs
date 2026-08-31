using System.Globalization;

namespace BuildingBlocks.Auditing;

/// <summary>
/// The single redaction, truncation and size policy for everything that reaches the audit trail — the
/// EF interceptor's before/after values and the free-form details a caller attaches.
/// </summary>
/// <remarks>
/// One policy, deliberately. An audit store's one hard guarantee is that it never becomes the place a
/// secret leaks to, and it is read by more people, for longer, than the database it describes. A second
/// copy of the marker list somewhere else is a second chance to forget a marker.
/// </remarks>
public static class AuditRedaction
{
    /// <summary>Longest value stored. Longer ones are cut and marked, so a truncated value never reads as whole.</summary>
    public const int MaxValueLength = 512;

    /// <summary>
    /// Most details kept on one record. A handler annotating once per row would otherwise write an
    /// unbounded record; past this the extra keys are dropped and <see cref="DroppedKey"/> says how many.
    /// </summary>
    public const int MaxDetails = 32;

    /// <summary>Added when details were dropped, so a capped record never looks complete.</summary>
    public const string DroppedKey = "detailsDropped";

    /// <summary>What a sensitive value is stored as.</summary>
    public const string RedactedValue = "***REDACTED***";

    // Names containing any of these (case-insensitively) are recorded as redacted.
    private static readonly string[] SensitiveMarkers =
        ["password", "secret", "token", "apikey", "api_key", "salt", "hash", "credential"];

    /// <summary>Whether a property or detail name looks like it names a secret.</summary>
    public static bool IsSensitive(string name)
    {
        foreach (var marker in SensitiveMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Cuts an over-long value and marks it, so nothing silently reads as complete.</summary>
    public static string? Truncate(string? value)
        => value is { Length: > MaxValueLength } ? value[..MaxValueLength] + "…" : value;

    /// <summary>
    /// Redacts a value whose name looks sensitive, otherwise truncates it. This is what makes a
    /// hand-written detail as safe as an intercepted column value.
    /// </summary>
    public static string? Sanitize(string name, string? value)
        => value is null ? null : IsSensitive(name) ? RedactedValue : Truncate(value);

    /// <summary>
    /// Sanitizes every detail and caps how many are kept. Returns null for nothing worth recording, so a
    /// record with no details carries no empty object.
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? Sanitize(IReadOnlyDictionary<string, string?>? details)
    {
        if (details is null or { Count: 0 })
        {
            return null;
        }

        var sanitized = new Dictionary<string, string?>(StringComparer.Ordinal);
        var dropped = 0;

        foreach (var (key, value) in details)
        {
            if (sanitized.Count >= MaxDetails)
            {
                dropped++;
                continue;
            }

            sanitized[key] = Sanitize(key, value);
        }

        if (dropped > 0)
        {
            sanitized[DroppedKey] = dropped.ToString(CultureInfo.InvariantCulture);
        }

        return sanitized;
    }
}
