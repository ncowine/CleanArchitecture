using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Students.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddStudentsApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
