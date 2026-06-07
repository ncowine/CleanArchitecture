using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class AcademicProgramConfiguration : IEntityTypeConfiguration<AcademicProgram>
{
    public void Configure(EntityTypeBuilder<AcademicProgram> builder)
    {
        builder.ToTable("Programs");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.Degree)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.DepartmentName)
            .IsRequired()
            .HasMaxLength(200);
    }
}
