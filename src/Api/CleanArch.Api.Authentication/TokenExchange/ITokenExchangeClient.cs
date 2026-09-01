namespace CleanArch.Api.Authentication;

/// <summary>
/// Exchanges a caller's access token for a downstream-scoped token via the OAuth2 token-exchange grant
/// (RFC 8693). The returned token preserves the original subject (the user) but carries the downstream
/// audience/scopes — this is what makes the outbound call act AS the user, not as the service. The
/// implementation is <see cref="TokenExchangeClient"/>, wrapped by
/// <see cref="CachingTokenExchangeClient"/>.
/// </summary>
internal interface ITokenExchangeClient
{
    /// <param name="subjectToken">The caller's validated incoming access token (the user's token).</param>
    /// <param name="downstream">Which downstream the token is for — the audience/scope to request.</param>
    /// <returns>The downstream-scoped token plus the instant it expires (so callers can cache it safely).</returns>
    /// <exception cref="TokenExchangeException">The provider rejected the subject token or was unavailable.</exception>
    Task<ExchangedToken> ExchangeAsync(
        string subjectToken, DownstreamTokenRequest downstream, CancellationToken cancellationToken);
}

/// <summary>
/// Identifies which downstream an exchange is for. Passed per call rather than read from configuration
/// once, because one API commonly calls SEVERAL downstreams and each needs its own audience — a token
/// minted for one is correctly rejected by the others.
/// </summary>
/// <param name="Name">The downstream's registered name (its <c>HttpClient</c> name). Used in logs, spans and cache keys.</param>
/// <param name="Audience">The <c>aud</c> to request. Optional — some setups infer it from the scopes.</param>
/// <param name="Scope">Space-delimited scopes to request. Optional.</param>
internal sealed record DownstreamTokenRequest(string Name, string? Audience, string? Scope);

/// <summary>A downstream-scoped access token and the absolute instant it expires.</summary>
internal sealed record ExchangedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Why a token exchange could not produce a usable downstream token.</summary>
public enum TokenExchangeFailure
{
    /// <summary>The provider rejected the subject token (e.g. expired, wrong audience) — a 4xx. Maps to 401.</summary>
    SubjectRejected,

    /// <summary>The provider could not be reached or errored (5xx / timeout / network) — maps to 502.</summary>
    ProviderUnavailable,
}

/// <summary>
/// Raised when the On-Behalf-Of exchange fails. Public so the API host can translate it into the right
/// HTTP status (a rejected subject token is the caller's problem; an unreachable provider is ours).
/// </summary>
public sealed class TokenExchangeException : Exception
{
    public TokenExchangeException(
        TokenExchangeFailure failure, string downstreamName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        DownstreamName = downstreamName;
    }

    public TokenExchangeFailure Failure { get; }

    /// <summary>
    /// Which downstream the failed exchange was for. With several downstreams configured, this is the
    /// first thing you need to know and the last thing a bare message tells you.
    /// </summary>
    public string DownstreamName { get; }
}
