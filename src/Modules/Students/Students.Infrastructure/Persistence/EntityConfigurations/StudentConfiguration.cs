using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("Students");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(s => s.Email).IsUnique();

        builder.Property(s => s.DateOfBirth).IsRequired();
        builder.Property(s => s.EnrolledOn).IsRequired();

        // Enum persisted as its name, not a magic number — readable rows, safe to reorder the enum.
        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // Owned value object: stored as columns on the Students table. Optional — a student may not
        // have an address on file yet.
        builder.OwnsOne(s => s.Address, address =>
        {
            address.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
            address.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(100);
            address.Property(a => a.State).HasColumnName("AddressState").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
            address.Property(a => a.Country).HasColumnName("AddressCountry").HasMaxLength(100);
        });

        // Owned child collection: emergency contacts have no life outside their student.
        builder.OwnsMany(s => s.EmergencyContacts, contact =>
        {
            contact.ToTable("EmergencyContacts");
            contact.WithOwner().HasForeignKey("StudentId");
            contact.HasKey(c => c.Id);
            contact.Property(c => c.Name).IsRequired().HasMaxLength(200);
            contact.Property(c => c.Relationship).IsRequired().HasMaxLength(50);
            contact.Property(c => c.PhoneNumber).IsRequired().HasMaxLength(30);
        });

        // Child entity with its own identity, but still part of the Student aggregate (no DbSet —
        // reached only through the student). Mapped via the backing field.
        builder.HasMany(s => s.Enrollments)
            .WithOne()
            .HasForeignKey("StudentId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Enrollments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
