using Equipment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Equipment.Infrastructure.Persistence.EntityConfigurations;

internal sealed class EquipmentAssetConfiguration : IEntityTypeConfiguration<EquipmentAsset>
{
    public void Configure(EntityTypeBuilder<EquipmentAsset> builder)
    {
        builder.ToTable("EquipmentAssets");

        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.Name).IsRequired().HasMaxLength(200);

        builder.Property(asset => asset.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(asset => asset.AssetTag).IsRequired().HasMaxLength(50);
        builder.HasIndex(asset => asset.AssetTag).IsUnique();

        builder.Property(asset => asset.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // Both the "who holds this" fact and the Equipment.Contracts idempotency key — indexed so
        // ReserveAsync's redelivery check (find the reservation already made for this request) is fast.
        builder.HasIndex(asset => asset.ReservedForOnboardingRequestId);

        // Resolved against SharedKernel's Sites at read time (see EquipmentDirectory) — not an EF
        // relationship, since Sites live in a different database entirely.
        builder.Property(asset => asset.SiteId);

        builder.Property(asset => asset.CreatedOnUtc).IsRequired();
    }
}
