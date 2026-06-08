using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Students.Application.Abstractions;
using Students.Application.Billing;

namespace Students.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddStudentsApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Application service: charge + threshold-driven financial hold, shared by every charge path.
        services.AddScoped<IAccountCharger, AccountCharger>();

        return services;
    }
}
