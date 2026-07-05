using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using TestPlans.Domain;

namespace TestPlans.Infrastructure.Persistence;

public sealed class TestPlansDbContext : DbContext
{
    public TestPlansDbContext(DbContextOptions<TestPlansDbContext> options) : base(options)
    {
    }

    public DbSet<TestPlan> Plans => Set<TestPlan>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<TestTask> Tasks => Set<TestTask>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<TestPlanVersion> Versions => Set<TestPlanVersion>();
    public DbSet<TaskResult> TaskResults => Set<TaskResult>();
    public DbSet<ActionLogEntry> ActionLog => Set<ActionLogEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TestPlansDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
