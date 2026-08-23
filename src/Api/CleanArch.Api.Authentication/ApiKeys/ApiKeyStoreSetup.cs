using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Production-safe operations on the API-key store. Unlike <see cref="ApiKeyDevelopmentSetup"/> these
/// never seed the well-known dev keys, so they are safe to run against a real deployment.
/// </summary>
public static class ApiKeyStoreSetup
{
    /// <summary>Applies the API-key store's pending migrations. Seeds nothing.</summary>
    public static async Task MigrateAsync(
        IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ApiKeyDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Mints a new API key for <paramref name="subject"/>, stores only its SHA-256 hash, and returns the
    /// raw key. This is the ONLY moment the raw key exists — it cannot be recovered afterwards, which is
    /// the point: a leak of the database does not leak usable keys. Hand it to the caller over a secure
    /// channel and store it in their secret manager.
    /// </summary>
    public static async Task<string> MintAsync(
        IServiceProvider services,
        string subject,
        string roles,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ApiKeyDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var (rawKey, prefix, hash) = ApiKeyHasher.Generate();

        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            Prefix = prefix,
            KeyHash = hash,
            Subject = subject,
            Roles = roles,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
        return rawKey;
    }
}
