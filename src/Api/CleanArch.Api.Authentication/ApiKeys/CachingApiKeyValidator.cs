using Microsoft.Extensions.Caching.Hybrid;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Short-TTL caching decorator over <see cref="DbApiKeyValidator"/>. Validation runs on every request,
/// so without this each call is a database round-trip. Mirrors <c>CachingUserDirectory</c>:
/// <see cref="HybridCache.GetOrCreateAsync"/> collapses concurrent misses for the same key into one
/// lookup (stampede protection). The cache key is the key's HASH — never the raw secret. The 1-minute
/// TTL bounds how long a revoked key keeps working. In-memory today; Redis-ready with no change here.
/// </summary>
internal sealed class CachingApiKeyValidator : IApiKeyValidator
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    private readonly DbApiKeyValidator _inner;
    private readonly HybridCache _cache;

    public CachingApiKeyValidator(DbApiKeyValidator inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<ApiKeyIdentity?> ValidateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult<ApiKeyIdentity?>(null);
        }

        return _cache.GetOrCreateAsync(
            $"apikey:{ApiKeyHasher.Hash(apiKey)}",
            (inner: _inner, apiKey),
            static (state, ct) => new ValueTask<ApiKeyIdentity?>(state.inner.ValidateAsync(state.apiKey, ct)),
            CacheOptions,
            cancellationToken: cancellationToken).AsTask();
    }
}
