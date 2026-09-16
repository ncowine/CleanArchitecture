using Microsoft.EntityFrameworkCore;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Host-owned persistence for API keys. It has its own database (see <c>ConnectionStrings:ApiKeys</c>),
/// isolated from every business module, with its own migrations-history table
/// (<see cref="MigrationsHistoryTable"/>) so this auth schema versions entirely independently.
/// </summary>
internal sealed class ApiKeyDbContext : DbContext
{
    /// <summary>Distinct history table so this context's migrations are self-contained even if it ever
    /// shares a physical database file with another context.</summary>
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
