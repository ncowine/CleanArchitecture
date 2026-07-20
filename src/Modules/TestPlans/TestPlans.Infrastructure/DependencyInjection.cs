using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestPlans.Application;
using TestPlans.Application.Abstractions;
using TestPlans.Contracts;
using TestPlans.Infrastructure.Behaviors;
using TestPlans.Infrastructure.Contracts;
using TestPlans.Infrastructure.Outbox;
using TestPlans.Infrastructure.Persistence;
using TestPlans.Infrastructure.Reads;
using TestPlans.Infrastructure.Repositories;

namespace TestPlans.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTestPlansModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddTestPlansApplication();

        // Audit change-tracking: capture before/after values of every write for the audit trail.
        services.AddAuditChangeTracking();
        services.AddDbContext<TestPlansDbContext>((sp, options) =>
            options.UseSqlite(connectionString).UseAuditChangeTracking(sp));

        // Authoring repositories + the shared result/action-log write path.
        services.AddScoped<ITestPlanRepository, EfTestPlanRepository>();
        services.AddScoped<ITestContentRepository, EfTestContentRepository>();
        services.AddScoped<IPlatformRepository, EfPlatformRepository>();
        services.AddScoped<ITaskResultStore, EfTaskResultStore>();

        // Published contracts: let the Tester Guide module read the catalog/results and sync actions back
        // into the primary action log without depending on this module's domain or DbContext.
        services.AddScoped<ITestPlanCatalog, TestPlanCatalog>();
        services.AddScoped<ITaskResultReader, TaskResultReader>();
        services.AddScoped<ITestPlanActionLog, TestPlanActionLog>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Sync saga — reverse leg: deliver MainDbActionRejected compensations back to the Guide module's
        // published reconciler contract.
        services.AddOutboxProcessing<TestPlansDbContext, TestPlansOutboxDispatcher>();

        return services;
    }
}
