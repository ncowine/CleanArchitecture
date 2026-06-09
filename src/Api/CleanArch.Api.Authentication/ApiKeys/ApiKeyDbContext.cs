using Microsoft.EntityFrameworkCore;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Host-owned persistence for API keys. It targets the SAME physical database as the Students module
/// (students.db), but it is a SEPARATE context with its own migrations-history table
/// (<see cref="MigrationsHistoryTable"/>), so this auth schema versions independently of the Students
/// domain model. That keeps an authentication concern out of the Students bounded context while still
/// honouring students.db as the primary database.
/// </summary>
internal sealed class ApiKeyDbContext : DbContext
{
    /// <summary>Distinct history table so this context's migrations never collide with the Students
    /// context's default <c>__EFMigrationsHistory</c> in the shared database file.</summary>
    public const string MigrationsHistoryTable = "__AuthMigrationsHistory";

    public ApiKeyDbContext(DbContextOptions<ApiKeyDbContext> options) : base(options)
    {
    }

    internal DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var key = modelBuilder.Entity<ApiKey>();
        key.ToTable("ApiKeys");
        key.HasKey(k => k.Id);

        // The hash is the lookup column: an exact-match unique index gives an O(log n) probe and no timing
        // side-channel — the comparison happens inside the index, not row-by-row in application code.
        key.HasIndex(k => k.KeyHash).IsUnique();
        key.HasIndex(k => k.Prefix);

        key.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();   // SHA-256 = 64 hex characters
        key.Property(k => k.Prefix).HasMaxLength(32).IsRequired();
        key.Property(k => k.Subject).HasMaxLength(128).IsRequired();
        key.Property(k => k.Roles).HasMaxLength(256).IsRequired();
    }
}
