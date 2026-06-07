using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Students.Application;
using Students.Application.Abstractions;
using Students.Infrastructure.Behaviors;
using Students.Infrastructure.Caching;
using Students.Infrastructure.Contracts;
using Students.Infrastructure.Outbox;
using Students.Infrastructure.Persistence;
using Students.Infrastructure.Reads;
using Students.Infrastructure.Repositories;
using Students.Contracts;

namespace Students.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStudentsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddStudentsApplication();

        services.AddDbContext<StudentsDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IStudentRepository, EfStudentRepository>();

        // Published contracts: let other modules read student reference data and request holds against
        // this module's DB without depending on its domain or DbContext.
        // IStudentDirectory is decorated with a cache (in-memory now, Redis-ready) since it's the hottest
        // read; the concrete StudentDirectory is the cache-miss fallthrough to the database.
        services.AddScoped<StudentDirectory>();
        services.AddScoped<IStudentDirectory>(provider => new CachingStudentDirectory(
            provider.GetRequiredService<StudentDirectory>(),
            provider.GetRequiredService<HybridCache>()));
        services.AddScoped<IStudentCacheInvalidator, StudentCacheInvalidator>();
        services.AddScoped<IStudentHoldService, StudentHoldService>();

        // Read side: purpose-built projections for this module's own endpoints (summary vs detail).
        services.AddScoped<IStudentReadService, StudentReadService>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Background dispatcher for the saga's reverse leg: delivers hold-rejection events back to the
        // Library module (which compensates by waiving the fines).
        services.AddOutboxProcessing<StudentsDbContext, StudentsOutboxDispatcher>();

        return services;
    }
}
