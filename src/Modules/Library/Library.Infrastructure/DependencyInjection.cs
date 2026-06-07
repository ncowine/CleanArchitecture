using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Library.Application;
using Library.Application.Abstractions;
using Library.Contracts;
using Library.Infrastructure.Behaviors;
using Library.Infrastructure.Contracts;
using Library.Infrastructure.Outbox;
using Library.Infrastructure.Persistence;
using Library.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLibraryModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddLibraryApplication();

        services.AddDbContext<LibraryDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ILoanRepository, EfLoanRepository>();

        // Shared outbox: writer (enqueue), admin (dead-letter read/replay), and the background
        // dispatcher wired to this module's dispatch logic.
        services.AddOutboxWriter<LibraryDbContext>();
        services.AddOutboxAdmin<LibraryDbContext>();
        services.AddOutboxProcessing<LibraryDbContext, LibraryOutboxDispatcher>();

        // Published compensation contract: the Students module calls this when it rejects a hold.
        services.AddScoped<IFineWaiver, FineWaiver>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
