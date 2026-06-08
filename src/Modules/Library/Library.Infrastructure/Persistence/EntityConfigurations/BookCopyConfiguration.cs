using Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Infrastructure.Persistence.EntityConfigurations;

internal sealed class BookCopyConfiguration : IEntityTypeConfiguration<BookCopy>
{
    public void Configure(EntityTypeBuilder<BookCopy> builder)
    {
        builder.ToTable("BookCopies");

        builder.HasKey(copy => copy.Id);

        // Reference to the Book aggregate by id — indexed for "copies of this book" lookups.
        builder.Property(copy => copy.BookId).IsRequired();
        builder.HasIndex(copy => copy.BookId);

        builder.Property(copy => copy.Barcode).IsRequired().HasMaxLength(50);
        builder.HasIndex(copy => copy.Barcode).IsUnique();

        builder.Property(copy => copy.Condition)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(copy => copy.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(copy => copy.AcquiredOn).IsRequired();

        builder.Ignore(copy => copy.IsAvailable);
    }
}
