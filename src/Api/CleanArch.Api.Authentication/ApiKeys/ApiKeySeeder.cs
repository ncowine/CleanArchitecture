using Microsoft.EntityFrameworkCore;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Seeds the well-known DEVELOPMENT API keys that the docs and Swagger reference, so a fresh local
/// database is immediately usable. Idempotent — each key is inserted only if its hash is absent. These
/// are dev-only convenience keys; production keys should be minted with <see cref="ApiKeyHasher.Generate"/>
/// and never seeded.
/// </summary>
internal static class ApiKeySeeder
{
    private static readonly (string RawKey, string Subject, string Roles)[] DevelopmentKeys =
    {
        ("dev-api-key-reporting", "reporting-service", "service"),
        ("dev-api-key-integration", "integration-service", "service"),
    };

    public static async Task SeedDevelopmentKeysAsync(ApiKeyDbContext db)
    {
        foreach (var (rawKey, subject, roles) in DevelopmentKeys)
        {
            var hash = ApiKeyHasher.Hash(rawKey);
            if (await db.ApiKeys.AnyAsync(k => k.KeyHash == hash))
            {
                continue;
            }

            db.ApiKeys.Add(new ApiKey
            {
                Id = Guid.NewGuid(),
                Prefix = ApiKeyHasher.DisplayPrefix(rawKey),
                KeyHash = hash,
                Subject = subject,
                Roles = roles,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}
