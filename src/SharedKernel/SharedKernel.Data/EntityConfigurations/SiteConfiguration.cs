using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Models;

namespace SharedKernel.Data.EntityConfigurations;

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("Sites");

        builder.HasKey(site => site.Id);

        builder.Property(site => site.Code).IsRequired().HasMaxLength(10);
        builder.HasIndex(site => site.Code).IsUnique();

        builder.Property(site => site.Name).IsRequired().HasMaxLength(100);
        builder.Property(site => site.City).IsRequired().HasMaxLength(100);

        // Static reference data, shipped with the migration itself rather than a dev-only seeder — this
        // list changes rarely enough that a schema change (migration) is an acceptable way to update it.
        builder.HasData(
            new { Id = SiteIds.London, Code = "LDN", Name = "London HQ", City = "London" },
            new { Id = SiteIds.NewYork, Code = "NYC", Name = "New York Office", City = "New York" },
            new { Id = SiteIds.Remote, Code = "RMT", Name = "Remote", City = "N/A" });
    }
}
