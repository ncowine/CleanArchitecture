using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestPlans.Domain;

namespace TestPlans.Infrastructure.Persistence.EntityConfigurations;

internal sealed class TestPlanVersionConfiguration : IEntityTypeConfiguration<TestPlanVersion>
{
    public void Configure(EntityTypeBuilder<TestPlanVersion> builder)
    {
        builder.ToTable("Versions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Version).IsRequired();
        builder.Property(v => v.SubVersion).IsRequired();

        // The Label is derived from Version/SubVersion — never persisted.
        builder.Ignore(v => v.Label);

        builder.HasIndex(v => new { v.TestPlanId, v.Version, v.SubVersion }).IsUnique();
    }
}
