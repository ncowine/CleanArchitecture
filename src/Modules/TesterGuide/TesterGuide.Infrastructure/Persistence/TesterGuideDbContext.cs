using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence;

public sealed class TesterGuideDbContext : DbContext
{
    public TesterGuideDbContext(DbContextOptions<TesterGuideDbContext> options) : base(options)
    {
    }

    public DbSet<Focus> Focuses => Set<Focus>();
    public DbSet<GuideConfig> Configs => Set<GuideConfig>();
    public DbSet<ConfigTemplate> Templates => Set<ConfigTemplate>();
    public DbSet<ConfigAssignment> Assignments => Set<ConfigAssignment>();
    public DbSet<ContentSelection> ContentSelections => Set<ContentSelection>();
    public DbSet<GuideActionLogEntry> ActionLog => Set<GuideActionLogEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TesterGuideDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
