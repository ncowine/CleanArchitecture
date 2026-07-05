using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence.EntityConfigurations;

internal sealed class FocusConfiguration : IEntityTypeConfiguration<Focus>
{
    public void Configure(EntityTypeBuilder<Focus> builder)
    {
        builder.ToTable("Focuses");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Description).IsRequired().HasMaxLength(500);

        builder.HasIndex(f => f.Name).IsUnique();
    }
}
