using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onboarding.Application;
using Onboarding.Application.Abstractions;
using Onboarding.Infrastructure.Behaviors;
using Onboarding.Infrastructure.Outbox;
using Onboarding.Infrastructure.Persistence;
using Onboarding.Infrastructure.Reads;
using Onboarding.Infrastructure.Repositories;
using Onboarding.Infrastructure.Services;

namespace Onboarding.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOnboardingModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddOnboardingApplication();

        // Audit change-tracking: capture before/after values of every write for the audit trail.
        services.AddAuditChangeTracking();
        services.AddDbContext<OnboardingDbContext>((sp, options) =>
            options.UseSqlite(connectionString).UseAuditChangeTracking(sp));

        services.AddScoped<IOnboardingRequestRepository, EfOnboardingRequestRepository>();
        services.AddScoped<IOnboardingSagaStateRepository, EfOnboardingSagaStateRepository>();
        services.AddScoped<IOnboardingReadService, OnboardingReadService>();

        // Simple service abstractions standing in for real integrations — not separate modules. Singleton
        // because their state (the licence pool, who holds what access) needs to survive across requests
        // within this process; it's a mock, so it does not need to survive a restart.
        services.AddSingleton<ILicenceAllocationService, InMemoryLicenceAllocationService>();
        services.AddSingleton<IAccessProvisioningService, InMemoryAccessProvisioningService>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // The Standard saga: a writer for the request handler to enqueue the first step, admin
        // read/replay over dead-lettered steps, and the background dispatcher that drives every step
        // (forward and compensating) via OnboardingOutboxDispatcher.
        services.AddOutboxWriter<OnboardingDbContext>();
        services.AddOutboxAdmin<OnboardingDbContext>();
        services.AddOutboxProcessing<OnboardingDbContext, OnboardingOutboxDispatcher>();

        return services;
    }
}
