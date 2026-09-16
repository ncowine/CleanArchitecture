using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Onboarding.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOnboardingApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
