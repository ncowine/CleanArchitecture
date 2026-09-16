using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onboarding.Domain;

namespace Onboarding.Infrastructure.Persistence.EntityConfigurations;

internal sealed class OnboardingRequestConfiguration : IEntityTypeConfiguration<OnboardingRequest>
{
    public void Configure(EntityTypeBuilder<OnboardingRequest> builder)
    {
        builder.ToTable("OnboardingRequests");

        builder.HasKey(request => request.Id);

        builder.Property(request => request.EmployeeName).IsRequired().HasMaxLength(200);
        builder.Property(request => request.RequiredEquipmentCategory).IsRequired().HasMaxLength(50);
        builder.Property(request => request.RequiredLicenceType).IsRequired().HasMaxLength(50);
        builder.Property(request => request.RequiredAccessLevel).IsRequired().HasMaxLength(50);

        builder.Property(request => request.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.FailureReason).HasMaxLength(500);
        builder.Property(request => request.CreatedOnUtc).IsRequired();

        builder.Property(request => request.EquipmentStepStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.LicenceStepStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.AccessStepStatus).IsRequired().HasConversion<string>().HasMaxLength(20);
    }
}
