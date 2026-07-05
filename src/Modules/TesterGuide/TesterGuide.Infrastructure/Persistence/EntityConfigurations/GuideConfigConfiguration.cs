using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence.EntityConfigurations;

internal sealed class GuideConfigConfiguration : IEntityTypeConfiguration<GuideConfig>
{
    public void Configure(EntityTypeBuilder<GuideConfig> builder)
    {
        builder.ToTable("Configs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CreatedBy).IsRequired().HasMaxLength(256);

        builder.Property(c => c.Mode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(c => c.TestPlanId);
    }
}
