using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TesterGuide.Application;
using TesterGuide.Application.Abstractions;
using TesterGuide.Contracts;
using TesterGuide.Infrastructure.Behaviors;
using TesterGuide.Infrastructure.Contracts;
using TesterGuide.Infrastructure.Outbox;
using TesterGuide.Infrastructure.Persistence;
using TesterGuide.Infrastructure.Reads;
using TesterGuide.Infrastructure.Repositories;

namespace TesterGuide.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTesterGuideModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddTesterGuideApplication();

        services.AddDbContext<TesterGuideDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IFocusRepository, EfFocusRepository>();
        services.AddScoped<IConfigTemplateRepository, EfConfigTemplateRepository>();
        services.AddScoped<IGuideConfigRepository, EfGuideConfigRepository>();
        services.AddScoped<IContentSelectionRepository, EfContentSelectionRepository>();
        services.AddScoped<IGuideActionLogRepository, EfGuideActionLogRepository>();

        services.AddScoped<IGuideReadService, GuideReadService>();

        // Cross-module reads (ITestPlanCatalog) are served by the Test Plans module, registered separately
        // in the composition root — this module depends only on the published contract.

        // Sync saga — forward leg: enqueue MainDbActionRequested (module-specific writer) and dispatch it to
        // the primary via ITestPlanActionLog; reverse leg: IGuideActionReconciler flags rejected syncs.
        services.AddScoped<ITesterGuideOutbox, TesterGuideOutbox>();
        services.AddScoped<IGuideActionReconciler, GuideActionReconciler>();
        services.AddOutboxProcessing<TesterGuideDbContext, TesterGuideOutboxDispatcher>();
        services.AddOutboxAdmin<TesterGuideDbContext>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
