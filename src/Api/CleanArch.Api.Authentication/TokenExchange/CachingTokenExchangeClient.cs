using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Caching decorator over <see cref="TokenExchangeClient"/>. A token exchange is a network round-trip to
/// the IdP, so without this every downstream call pays for one. Mirrors <see cref="CachingApiKeyValidator"/>:
/// <see cref="HybridCache.GetOrCreateAsync"/> collapses concurrent misses into one exchange (stampede
/// protection). Three correctness rules specific to tokens:
/// <list type="bullet">
/// <item>The key includes the downstream's name, audience and scope, not just the subject-token hash —
/// otherwise a second downstream API would collide on the same user's entry and receive a token minted
/// for the wrong audience, which it would (correctly) reject. Audience and scope are in the key as well
/// as the name so that re-pointing a downstream in configuration cannot serve tokens for the old one.</item>
/// <item>The entry is LOCAL-ONLY (<see cref="HybridCacheEntryFlags.DisableDistributedCache"/>): these are
/// live user bearer tokens and must never be written to a shared L2 (Redis) in plaintext.</item>
/// <item>A cached token is re-exchanged once it is within the configured skew of its real expiry, so we
/// never hand the downstream a token that is about to (or already has) expired.</item>
/// </list>
/// The cache key is the HASH of the subject token — never the raw token.
/// </summary>
internal sealed class CachingTokenExchangeClient : ITokenExchangeClient
{
    // Local-only (never L2/Redis) upper bound on how long an entry lingers; the real gate is the per-token
    // expiry recheck below, which adapts to whatever lifetime the provider issued.
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(30),
        Flags = HybridCacheEntryFlags.DisableDistributedCache,
    };

    private readonly TokenExchangeClient _inner;
    private readonly HybridCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _skew;

    public CachingTokenExchangeClient(
        TokenExchangeClient inner,
        HybridCache cache,
        IOptions<OnBehalfOfOptions> options,
        TimeProvider timeProvider)
    {
        _inner = inner;
        _cache = cache;
        _timeProvider = timeProvider;
        _skew = TimeSpan.FromSeconds(options.Value.ExpirySkewSeconds);
    }

    public async Task<ExchangedToken> ExchangeAsync(
        string subjectToken, DownstreamTokenRequest downstream, CancellationToken cancellationToken)
    {
        var key = $"obo:{downstream.Name}:{downstream.Audience}:{downstream.Scope}:{ApiKeyHasher.Hash(subjectToken)}";

        var token = await GetOrExchangeAsync(key, subjectToken, downstream, cancellationToken);

        // The cached token may have been minted with a short lifetime, or simply aged out within the
        // entry's lingering window. If it is within the skew of expiry, drop it and exchange once more.
        if (IsExpiring(token))
        {
            await _cache.RemoveAsync(key, cancellationToken);
            token = await GetOrExchangeAsync(key, subjectToken, downstream, cancellationToken);
        }

        return token;
    }

    private ValueTask<ExchangedToken> GetOrExchangeAsync(
        string key,
        string subjectToken,
        DownstreamTokenRequest downstream,
        CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            key,
            (inner: _inner, subjectToken, downstream),
            static (state, ct) => new ValueTask<ExchangedToken>(
                state.inner.ExchangeAsync(state.subjectToken, state.downstream, ct)),
            CacheOptions,
            cancellationToken: cancellationToken);

    private bool IsExpiring(ExchangedToken token) =>
        token.ExpiresAtUtc - _skew <= _timeProvider.GetUtcNow();
}
