using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.Credits).IsRequired();
        builder.Property(c => c.DepartmentName).IsRequired().HasMaxLength(200);

        // Owned child collection: prerequisite links live only inside their course. Each references the
        // prerequisite course by id (cross-aggregate) — indexed, but no FK navigation to Courses.
        builder.OwnsMany(c => c.Prerequisites, prerequisite =>
        {
            prerequisite.ToTable("CoursePrerequisites");
            prerequisite.WithOwner().HasForeignKey("CourseId");
            prerequisite.HasKey(p => p.Id);

            // Domain-assigned Id (not store-generated) — otherwise adding a prerequisite to an existing course
            // UPDATEs a nonexistent row → concurrency exception. See StudentAccountConfiguration.
            prerequisite.Property(p => p.Id).ValueGeneratedNever();
            prerequisite.Property(p => p.PrerequisiteCourseId).IsRequired();
            prerequisite.HasIndex(p => p.PrerequisiteCourseId);
        });
    }
}
