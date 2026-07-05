using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestPlans.Domain;

namespace TestPlans.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ActionLogEntryConfiguration : IEntityTypeConfiguration<ActionLogEntry>
{
    public void Configure(EntityTypeBuilder<ActionLogEntry> builder)
    {
        builder.ToTable("ActionLog");

        // Id is caller-supplied (the outbox message id for synced actions), so it is never generated.
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.ActorId).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OccurredOnUtc).IsRequired();

        // History lookups by the (task, platform, version) coordinate.
        builder.HasIndex(e => new { e.TestTaskId, e.PlatformId, e.TestPlanVersionId });
    }
}
