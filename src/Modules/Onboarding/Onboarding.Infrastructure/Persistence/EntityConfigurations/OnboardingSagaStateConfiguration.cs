using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onboarding.Domain;

namespace Onboarding.Infrastructure.Persistence.EntityConfigurations;

internal sealed class OnboardingSagaStateConfiguration : IEntityTypeConfiguration<OnboardingSagaState>
{
    public void Configure(EntityTypeBuilder<OnboardingSagaState> builder)
    {
        builder.ToTable("OnboardingSagaStates");

        builder.HasKey(state => state.Id);

        // One saga per request — also how the dispatcher looks up the saga for a given message.
        builder.HasIndex(state => state.OnboardingRequestId).IsUnique();

        builder.Property(state => state.CurrentStep).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(state => state.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(state => state.StartedOnUtc).IsRequired();
    }
}
