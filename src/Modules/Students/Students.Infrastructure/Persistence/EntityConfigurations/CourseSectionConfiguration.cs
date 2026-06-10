using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class CourseSectionConfiguration : IEntityTypeConfiguration<CourseSection>
{
    public void Configure(EntityTypeBuilder<CourseSection> builder)
    {
        builder.ToTable("CourseSections");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.CourseId).IsRequired();
        builder.HasIndex(s => s.CourseId);

        builder.Property(s => s.InstructorId).IsRequired();
        builder.HasIndex(s => s.InstructorId);

        builder.Property(s => s.Term).IsRequired().HasMaxLength(50);
        builder.HasIndex(s => s.Term);

        builder.Property(s => s.SectionCode).IsRequired().HasMaxLength(20);
        builder.Property(s => s.Capacity).IsRequired();

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // Computed from the roster — not stored.
        builder.Ignore(s => s.EnrolledCount);
        builder.Ignore(s => s.WaitlistCount);

        // Owned value object: schedule flattened as columns on the section row.
        builder.OwnsOne(s => s.Schedule, schedule =>
        {
            schedule.Property(sc => sc.Days)
                .HasColumnName("ScheduleDays")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(100);
            schedule.Property(sc => sc.StartTime).HasColumnName("ScheduleStartTime").IsRequired();
            schedule.Property(sc => sc.EndTime).HasColumnName("ScheduleEndTime").IsRequired();
            schedule.Property(sc => sc.Room).HasColumnName("ScheduleRoom").IsRequired().HasMaxLength(50);
        });

        // Owned child collection: roster entries have no life outside their section. The student is
        // referenced by id only (a separate aggregate) — indexed, no FK navigation to Students.
        builder.OwnsMany(s => s.Roster, enrollment =>
        {
            enrollment.ToTable("SectionEnrollments");
            enrollment.WithOwner().HasForeignKey("SectionId");
            enrollment.HasKey(e => e.Id);

            // Domain-assigned Id (not store-generated). Without this, EF's Guid-key convention treats a new
            // roster entry added to an already-loaded section as an existing row → UPDATE (0 rows) → concurrency
            // exception; e.g. the second enrolment into a section would fail. See StudentAccountConfiguration.
            enrollment.Property(e => e.Id).ValueGeneratedNever();
            enrollment.Property(e => e.StudentId).IsRequired();
            enrollment.HasIndex(e => e.StudentId);
            enrollment.Property(e => e.EnrolledOn).IsRequired();
            enrollment.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            enrollment.Property(e => e.WaitlistPosition);

            // Nested optional owned value object: the grade, flattened onto the SectionEnrollments table.
            // Null until the enrollment is graded.
            enrollment.OwnsOne(e => e.Grade, grade =>
            {
                grade.Property(g => g.Letter).HasColumnName("GradeLetter").HasMaxLength(2);
                grade.Property(g => g.Points).HasColumnName("GradePoints").HasPrecision(3, 2);
            });
        });
    }
}
