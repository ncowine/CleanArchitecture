using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class StudentAccountConfiguration : IEntityTypeConfiguration<StudentAccount>
{
    public void Configure(EntityTypeBuilder<StudentAccount> builder)
    {
        builder.ToTable("StudentAccounts");

        builder.HasKey(account => account.Id);

        // One account per student — referenced by id (the Student is a separate aggregate).
        builder.Property(account => account.StudentId).IsRequired();
        builder.HasIndex(account => account.StudentId).IsUnique();

        builder.Property(account => account.Balance).IsRequired().HasPrecision(18, 2);

        // Owned ledger: entries have no life outside their account.
        builder.OwnsMany(account => account.Entries, entry =>
        {
            entry.ToTable("AccountEntries");
            entry.WithOwner().HasForeignKey("AccountId");
            entry.HasKey(e => e.Id);

            // The Id is assigned by the domain (Guid.NewGuid() in the constructor), NOT store-generated.
            // Without this, EF's Guid-key convention (ValueGeneratedOnAdd) treats a new entry added to an
            // already-loaded account as an existing row → UPDATE (0 rows) → DbUpdateConcurrencyException,
            // instead of an INSERT. The first entry only works because the whole account graph is new.
            entry.Property(e => e.Id).ValueGeneratedNever();

            entry.Property(e => e.Kind)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entry.Property(e => e.Category)
                .HasConversion<string>()
                .HasMaxLength(20);

            entry.Property(e => e.Amount).IsRequired().HasPrecision(18, 2);
            entry.Property(e => e.Description).IsRequired().HasMaxLength(200);
            entry.Property(e => e.OccurredOn).IsRequired();

            // Idempotency key for cross-module charges (e.g. a library fine); indexed for the lookup.
            entry.Property(e => e.SourceReference);
            entry.HasIndex(e => e.SourceReference);
        });
    }
}
