using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps the shared <see cref="OutboxMessage"/> into the model. Call from a DbContext's
    /// <c>OnModelCreating</c> — the configuration lives in this assembly, so each context must apply
    /// it explicitly (ApplyConfigurationsFromAssembly only scans the context's own assembly).
    /// </summary>
    public static ModelBuilder ApplyOutboxConfiguration(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }
}
