using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLibraryApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
