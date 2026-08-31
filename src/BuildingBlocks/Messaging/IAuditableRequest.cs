using BuildingBlocks.Auditing;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Marks a request whose execution should be recorded to the audit sink. The audit behavior wraps only
/// requests carrying this marker, so ordinary traffic isn't audited. On its own it means a write; use
/// <see cref="IAuditableRead"/> for a data-access request that should be audited too.
/// </summary>
public interface IAuditableRequest
{
    /// <summary>
    /// Optionally names what this request touched, so the record answers "whose data?" and not just
    /// "which action?" — e.g. <c>$"Student/{StudentId}"</c>. Defaults to null (nothing named).
    /// </summary>
    string? AuditResource => null;
}

/// <summary>
/// Marks a query whose execution should be audited — the "who read this?" half of an audit trail, which
/// regulated data (health, finance, PII) usually needs as much as "who changed this?". Recorded with
/// <see cref="AuditCategory.Read"/>, so reads stay filterable apart from writes.
/// </summary>
/// <remarks>
/// Opt in per query, deliberately: reads outnumber writes by orders of magnitude, and auditing all of
/// them buries the records that matter (and costs real storage). Mark the queries that expose data
/// someone could be asked to account for having looked at.
/// </remarks>
public interface IAuditableRead : IAuditableRequest;
