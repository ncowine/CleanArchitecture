using Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Infrastructure.Persistence.EntityConfigurations;

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");

        builder.HasKey(loan => loan.Id);

        // The key of the student in the *other* (Students) database. Indexed because we always query
        // loans by student — but deliberately NOT a foreign key, since that table lives in a
        // different database and no relational FK can span databases.
        builder.Property(loan => loan.StudentId).IsRequired();
        builder.HasIndex(loan => loan.StudentId);

        builder.Property(loan => loan.BookTitle)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(loan => loan.BorrowedOn).IsRequired();
        builder.Property(loan => loan.DueOn).IsRequired();

        builder.Property(loan => loan.FineAmount)
            .IsRequired()
            .HasPrecision(18, 2);
    }
}
