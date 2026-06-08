using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Application.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLibraryApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Pure application service (no infrastructure deps beyond IReservationRepository), so it's
        // registered here rather than in the infrastructure module.
        services.AddScoped<IReservationAllocator, ReservationAllocator>();

        return services;
    }
}
