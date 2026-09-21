using Microsoft.EntityFrameworkCore;
using SharedKernel.Models;

namespace SharedKernel.Data;

public sealed class ReferenceDbContext : DbContext
{
    public ReferenceDbContext(DbContextOptions<ReferenceDbContext> options) : base(options)
    {
    }

    public DbSet<Site> Sites => Set<Site>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReferenceDbContext).Assembly);
    }
}
