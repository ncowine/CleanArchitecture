using Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.BookId).IsRequired();
        builder.HasIndex(reservation => reservation.BookId);

        builder.Property(reservation => reservation.StudentId).IsRequired();
        builder.HasIndex(reservation => reservation.StudentId);

        builder.Property(reservation => reservation.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(reservation => reservation.QueuePosition);
        builder.Property(reservation => reservation.HeldCopyId);
        builder.Property(reservation => reservation.ReservedOn).IsRequired();
        builder.Property(reservation => reservation.ReadyOn);
        builder.Property(reservation => reservation.ExpiresOn);

        builder.Ignore(reservation => reservation.IsActive);
    }
}
