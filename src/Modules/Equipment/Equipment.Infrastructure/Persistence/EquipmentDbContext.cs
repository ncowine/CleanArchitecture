using BuildingBlocks.Outbox;
using Equipment.Domain;
using Microsoft.EntityFrameworkCore;

namespace Equipment.Infrastructure.Persistence;

public sealed class EquipmentDbContext : DbContext
{
    public EquipmentDbContext(DbContextOptions<EquipmentDbContext> options) : base(options)
    {
    }

    public DbSet<EquipmentAsset> Equipment => Set<EquipmentAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EquipmentDbContext).Assembly);

        // The module's outbox table: integration events for other applications, saved in the same transaction as
        // the change (see IEquipmentOutbox).
        modelBuilder.ApplyOutboxConfiguration();
    }
}
