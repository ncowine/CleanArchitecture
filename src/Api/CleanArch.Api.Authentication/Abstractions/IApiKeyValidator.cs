namespace CleanArch.Api.Authentication;

/// <summary>
/// Validates a presented API key against the store. Returns the resolved identity for a valid,
/// unexpired, unrevoked key; otherwise <c>null</c>. The DB-backed implementation is
/// <see cref="DbApiKeyValidator"/>, wrapped by <see cref="CachingApiKeyValidator"/>.
/// </summary>
public interface IApiKeyValidator
{
    Task<ApiKeyIdentity?> ValidateAsync(string apiKey, CancellationToken cancellationToken);
}

/// <summary>
/// The resolved identity behind a valid API key — a small serializable projection (so it can be cached)
/// from which the authentication handler builds the <c>ClaimsPrincipal</c>.
/// </summary>
public sealed record ApiKeyIdentity(string Subject, IReadOnlyList<string> Roles);
