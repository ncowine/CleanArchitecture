using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Students.Domain;

namespace Students.Infrastructure.Persistence.EntityConfigurations;

internal sealed class StudentHoldConfiguration : IEntityTypeConfiguration<StudentHold>
{
    public void Configure(EntityTypeBuilder<StudentHold> builder)
    {
        builder.ToTable("StudentHolds");

        // The key is the originating outbox message id — see StudentHold. The primary key doubles as
        // the idempotency guard: a duplicate delivery can't insert a second row with the same id.
        builder.HasKey(hold => hold.Id);

        builder.Property(hold => hold.StudentId).IsRequired();
        builder.HasIndex(hold => hold.StudentId);

        builder.Property(hold => hold.Reason)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(hold => hold.PlacedOnUtc).IsRequired();
    }
}
