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

        // The copy borrowed — a BookCopy aggregate in this same DB, referenced by id (indexed for the
        // "is this copy on loan?" lookup). Not a relational FK: aggregates are referenced by id only.
        builder.Property(loan => loan.CopyId).IsRequired();
        builder.HasIndex(loan => loan.CopyId);

        builder.Property(loan => loan.BorrowedOn).IsRequired();
        builder.Property(loan => loan.DueOn).IsRequired();
        builder.Property(loan => loan.ReturnedOn);
        builder.Property(loan => loan.RenewalCount).IsRequired();

        builder.Property(loan => loan.FineAmount)
            .IsRequired()
            .HasPrecision(18, 2);
    }
}
