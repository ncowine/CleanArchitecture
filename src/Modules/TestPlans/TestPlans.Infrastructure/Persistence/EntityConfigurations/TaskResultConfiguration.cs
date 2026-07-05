using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestPlans.Domain;

namespace TestPlans.Infrastructure.Persistence.EntityConfigurations;

internal sealed class TaskResultConfiguration : IEntityTypeConfiguration<TaskResult>
{
    public void Configure(EntityTypeBuilder<TaskResult> builder)
    {
        builder.ToTable("TaskResults");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.ActorId).IsRequired().HasMaxLength(256);
        builder.Property(r => r.ActionedOnUtc).IsRequired();

        // One current result per (task, platform, version).
        builder.HasIndex(r => new { r.TestTaskId, r.PlatformId, r.TestPlanVersionId }).IsUnique();
    }
}
