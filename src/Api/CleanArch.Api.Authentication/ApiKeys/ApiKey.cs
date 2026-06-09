namespace CleanArch.Api.Authentication;

/// <summary>
/// A persisted API key. The raw key is NEVER stored — only its SHA-256 hash (<see cref="KeyHash"/>),
/// which is also the lookup column. <see cref="Prefix"/> holds the first few non-secret characters in
/// clear, purely for display/audit (dashboards, logs). Expiry and revocation are enforced at validation.
/// </summary>
internal sealed class ApiKey
{
    public Guid Id { get; set; }

    /// <summary>Visible, non-secret leading characters (e.g. "ca_live_a1b2") — for dashboards/logs only.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 of the full raw key (uppercase hex). The unique, indexed lookup column.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>The principal this key authenticates as — becomes the Name claim and the audit actor.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Comma-separated roles granted to the key (e.g. "service" or "service,reporting").</summary>
    public string Roles { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Optional expiry. A key past this instant is rejected as if it did not exist.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Set to revoke the key immediately (subject to the short validation-cache TTL).</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Best-effort "last seen" stamp, updated on a successful cache-miss validation.</summary>
    public DateTime? LastUsedAtUtc { get; set; }
}
