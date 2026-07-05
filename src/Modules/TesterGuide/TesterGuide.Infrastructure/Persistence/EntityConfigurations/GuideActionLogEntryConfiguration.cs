using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence.EntityConfigurations;

internal sealed class GuideActionLogEntryConfiguration : IEntityTypeConfiguration<GuideActionLogEntry>
{
    public void Configure(EntityTypeBuilder<GuideActionLogEntry> builder)
    {
        builder.ToTable("GuideActionLog");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.SyncState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.UserId).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OccurredOnUtc).IsRequired();
        builder.Property(e => e.SyncError).HasMaxLength(1000);

        builder.HasIndex(e => e.GuideConfigId);
    }
}
