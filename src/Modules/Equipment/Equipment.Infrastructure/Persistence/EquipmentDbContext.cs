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
    }
}
