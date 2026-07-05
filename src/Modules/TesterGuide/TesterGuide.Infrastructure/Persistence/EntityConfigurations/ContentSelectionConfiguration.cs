using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TesterGuide.Domain;

namespace TesterGuide.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ContentSelectionConfiguration : IEntityTypeConfiguration<ContentSelection>
{
    public void Configure(EntityTypeBuilder<ContentSelection> builder)
    {
        builder.ToTable("ContentSelections");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.IsEnabled).IsRequired();

        // One overlay row per (test plan, task).
        builder.HasIndex(s => new { s.TestPlanId, s.TestTaskId }).IsUnique();
    }
}
