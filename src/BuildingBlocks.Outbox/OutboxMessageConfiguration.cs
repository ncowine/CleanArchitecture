using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(message => message.Content).IsRequired();
        builder.Property(message => message.OccurredOnUtc).IsRequired();
        builder.Property(message => message.Attempts).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(100);

        // The dispatcher polls for messages that are neither delivered nor dead-lettered.
        builder.HasIndex(message => new { message.ProcessedOnUtc, message.DeadLetteredOnUtc });
    }
}
