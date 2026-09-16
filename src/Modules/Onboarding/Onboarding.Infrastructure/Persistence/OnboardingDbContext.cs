using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Onboarding.Domain;

namespace Onboarding.Infrastructure.Persistence;

public sealed class OnboardingDbContext : DbContext
{
    public OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : base(options)
    {
    }

    public DbSet<OnboardingRequest> Requests => Set<OnboardingRequest>();
    public DbSet<OnboardingSagaState> SagaStates => Set<OnboardingSagaState>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnboardingDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
