using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArch.Api.Authentication;

/// <summary>
/// DEVELOPMENT helper: applies the API-key store's migrations and seeds the well-known dev keys. Lives
/// in the auth project so the host can set up the store without referencing the internal
/// <c>ApiKeyDbContext</c> or seeder. Call only in Development.
/// </summary>
public static class ApiKeyDevelopmentSetup
{
    public static async Task MigrateAndSeedAsync(
        IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ApiKeyDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await ApiKeySeeder.SeedDevelopmentKeysAsync(db);
    }
}
