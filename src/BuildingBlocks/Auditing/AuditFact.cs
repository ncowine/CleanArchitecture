namespace BuildingBlocks.Auditing;

/// <summary>
/// The caller-supplied half of an audit record: what happened and where. The ambient half — who, the
/// correlation id, and when — is filled in by <see cref="IAuditRecorder"/>, so call sites stay one line.
/// </summary>
/// <example>
/// <code>
/// await recorder.RecordAsync(new AuditFact("CreditScoreLookup")
/// {
///     Category = AuditCategory.External,
///     Source = "Api:CreditBureau",
///     Resource = $"Student/{studentId}",
/// }.With("bureauReference", reference), cancellationToken);
/// </code>
/// </example>
public sealed record AuditFact(string Action)
{
    /// <summary>Defaults to <see cref="AuditCategory.Custom"/> — set it to say what kind of activity this was.</summary>
    public AuditCategory Category { get; init; } = AuditCategory.Custom;

    /// <summary>The system the data came from, when it isn't our own database. See <see cref="AuditEntry.Source"/>.</summary>
    public string? Source { get; init; }

    /// <summary>What was accessed, as a searchable identifier. See <see cref="AuditEntry.Resource"/>.</summary>
    public string? Resource { get; init; }

    /// <summary>Outcome. <c>TrackAsync</c> sets this for you from whether the operation threw.</summary>
    public bool Succeeded { get; init; } = true;

    /// <summary>Failure reason when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>How long it took. <c>TrackAsync</c> measures this for you.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>Custom facts to record alongside the entry — ids, row counts, upstream request ids.</summary>
    public IReadOnlyDictionary<string, string?> Details { get; init; } = EmptyDetails;

    private static readonly IReadOnlyDictionary<string, string?> EmptyDetails =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Returns a copy with one more detail attached, so facts can be built up inline without a
    /// dictionary literal at every call site. Record facts about the call — a row count, a vendor
    /// request id — not the payload of it. <see cref="AuditRedaction"/> redacts values whose key names a
    /// secret and truncates long ones, but it cannot recognise a payload you chose to pass.
    /// </summary>
    public AuditFact With(string key, string? value)
    {
        var details = new Dictionary<string, string?>(Details, StringComparer.Ordinal) { [key] = value };
        return this with { Details = details };
    }
}
