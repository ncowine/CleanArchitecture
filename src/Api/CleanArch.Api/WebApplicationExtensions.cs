using BuildingBlocks.Outbox;
using CleanArch.Api.Authentication;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Students.Infrastructure.Persistence;

namespace CleanArch.Api;

internal static class WebApplicationExtensions
{
    /// <summary>
    /// Development-only host setup: applies pending migrations, exposes Swagger UI, and maps a couple
    /// of diagnostic endpoints. No-op outside Development. (Applying migrations at startup is a
    /// convenience for local runs — in production migrations should be a separate deploy step.)
    /// </summary>
    public static async Task UseDevelopmentSetupAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();

        // Each database is migrated independently — they share nothing, not even a transaction.
        await scope.ServiceProvider.GetRequiredService<StudentsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();

        // The API-key store shares students.db but migrates on its own history table. The auth project
        // owns its migrate+seed, so the host needn't touch the internal context/seeder.
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
