using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class InstructorConfiguration : IEntityTypeConfiguration<Instructor>
{
    public void Configure(EntityTypeBuilder<Instructor> builder)
    {
        builder.ToTable("Instructors");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(i => i.LastName).IsRequired().HasMaxLength(100);

        builder.Property(i => i.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(i => i.Email).IsUnique();

        builder.Property(i => i.DepartmentName).IsRequired().HasMaxLength(200);

        builder.Property(i => i.Rank)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);
    }
}
