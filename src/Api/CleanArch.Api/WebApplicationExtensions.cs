using BuildingBlocks.Outbox;
using CleanArch.Api.Authentication;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Students.Infrastructure.Persistence;
using TestPlans.Infrastructure.Persistence;
using TestPlans.Infrastructure.Seed;
using TesterGuide.Infrastructure.Persistence;

namespace CleanArch.Api;

internal static class WebApplicationExtensions
{
    /// <summary>
    /// Applies pending migrations for every database. Runs unconditionally in Development (local
    /// convenience); outside Development it is OPT-IN via <c>Database:MigrateOnStartup</c>, because
    /// changing a production schema is a deployment decision rather than a side effect of booting.
    ///
    /// Seeds nothing — no dev API keys, no sample content — so it is safe against a real deployment.
    ///
    /// CAVEAT: this migrates from inside the app process, which assumes ONE instance starts at a time.
    /// If you ever scale to multiple replicas, two of them can race on the same schema; at that point
    /// move migrations to a separate one-shot step that runs before the new version rolls out.
    /// </summary>
    public static async Task UseDatabaseMigrationsAsync(this WebApplication app)
    {
        var shouldMigrate = app.Environment.IsDevelopment()
            || app.Configuration.GetValue<bool>("Database:MigrateOnStartup");

        if (!shouldMigrate)
        {
            return;
        }

        using var scope = app.Services.CreateScope();

        // Each database is migrated independently — they share nothing, not even a transaction.
        await scope.ServiceProvider.GetRequiredService<StudentsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TestPlansDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TesterGuideDbContext>().Database.MigrateAsync();

        // The API-key store shares students.db but migrates on its own history table. The auth project
        // owns the migration so the host needn't touch the internal context.
        await ApiKeyStoreSetup.MigrateAsync(scope.ServiceProvider);
    }

    /// <summary>
    /// Development-only host setup: seeds sample data and the well-known dev API keys, exposes Swagger
    /// UI, and maps a couple of diagnostic endpoints. No-op outside Development. Migrations are handled
    /// separately by <see cref="UseDatabaseMigrationsAsync"/>, which runs first.
    /// </summary>
    public static async Task UseDevelopmentSetupAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();

        // Seed a small Test Plans content tree so the stand-in system of record has something to reference.
        await TestPlansSeeder.SeedAsync(scope.ServiceProvider);

        // Well-known dev keys — convenience for local runs and the docs. Deliberately Development-only:
        // these values are published in the README, so seeding them anywhere reachable is a backdoor.
        await ApiKeyDevelopmentSetup.MigrateAndSeedAsync(scope.ServiceProvider);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "CleanArchitecture API v1");
            options.RoutePrefix = "swagger";
        });

        // DEV-ONLY diagnostic: enqueue an unroutable outbox message. The dispatcher can't handle its
        // type, so it fails every attempt and ends up dead-lettered — a way to exercise that path.
        app.MapPost("/library/outbox/_dev/poison", async (
            LibraryDbContext db,
            CancellationToken cancellationToken) =>
        {
            var id = Guid.NewGuid();
            db.Outbox.Add(new OutboxMessage
            {
                Id = id,
                Type = "UnroutableTestMessage",
                Content = "{}",
                OccurredOnUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id });
        })
        .WithName("InjectPoisonOutboxMessage")
        .WithSummary("DEV ONLY: enqueue an unroutable message to exercise the retry + dead-letter path.")
        .WithTags("Library — Outbox");
    }
}
