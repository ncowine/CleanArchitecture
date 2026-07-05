using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Messaging;

namespace TestPlans.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTestPlansApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
