using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ConfigAssignmentConfiguration : IEntityTypeConfiguration<ConfigAssignment>
{
    public void Configure(EntityTypeBuilder<ConfigAssignment> builder)
    {
        builder.ToTable("ConfigAssignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).IsRequired().HasMaxLength(256);
        builder.Property(a => a.DisplayName).IsRequired().HasMaxLength(256);

        builder.Property(a => a.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.AssignedOnUtc).IsRequired();

        // A user is assigned to a config at most once.
        builder.HasIndex(a => new { a.GuideConfigId, a.UserId }).IsUnique();
    }
}
