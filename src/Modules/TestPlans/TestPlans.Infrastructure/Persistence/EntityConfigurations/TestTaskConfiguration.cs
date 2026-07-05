using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestPlans.Domain;

namespace TestPlans.Infrastructure.Persistence.EntityConfigurations;

internal sealed class TestTaskConfiguration : IEntityTypeConfiguration<TestTask>
{
    public void Configure(EntityTypeBuilder<TestTask> builder)
    {
        builder.ToTable("Tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(2000);

        builder.Property(t => t.Mode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(t => t.SubCategoryId);
    }
}
