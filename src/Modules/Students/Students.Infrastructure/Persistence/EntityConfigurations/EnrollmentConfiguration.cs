using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProgramId).IsRequired();

        builder.Property(e => e.Term)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.EnrolledOn).IsRequired();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.Grade).HasMaxLength(5);

        // Cross-aggregate reference by id — indexed for lookups, but no FK navigation to Programs.
        builder.HasIndex(e => e.ProgramId);
    }
}
