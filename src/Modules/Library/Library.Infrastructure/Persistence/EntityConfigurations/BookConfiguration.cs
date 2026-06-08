using Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Infrastructure.Persistence.EntityConfigurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");

        builder.HasKey(book => book.Id);

        builder.Property(book => book.Isbn).IsRequired().HasMaxLength(20);
        builder.HasIndex(book => book.Isbn).IsUnique();

        builder.Property(book => book.Title).IsRequired().HasMaxLength(300);
        builder.HasIndex(book => book.Title);

        builder.Property(book => book.Author).IsRequired().HasMaxLength(200);

        builder.Property(book => book.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(book => book.PublishedYear).IsRequired();
        builder.Property(book => book.Description).HasMaxLength(2000);
    }
}
