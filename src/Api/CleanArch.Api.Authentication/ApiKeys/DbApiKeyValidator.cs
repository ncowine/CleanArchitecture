using Microsoft.EntityFrameworkCore;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Validates a presented API key against the database: hash it, look the hash up by its unique index,
/// and reject it if the key is missing, revoked, or expired. On a successful hit it stamps LastUsedAt
/// (best-effort). Wrapped by <see cref="CachingApiKeyValidator"/>, so it only runs on a cache miss.
/// </summary>
internal sealed class DbApiKeyValidator : IApiKeyValidator
{
    private readonly ApiKeyDbContext _db;

    public DbApiKeyValidator(ApiKeyDbContext db) => _db = db;

    public async Task<ApiKeyIdentity?> ValidateAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var hash = ApiKeyHasher.Hash(apiKey);
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, cancellationToken);

        if (key is null ||
            key.RevokedAtUtc is not null ||
            (key.ExpiresAtUtc is not null && key.ExpiresAtUtc <= DateTime.UtcNow))
        {
            return null;
        }

        key.LastUsedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var roles = key.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ApiKeyIdentity(key.Subject, roles);
    }
}
