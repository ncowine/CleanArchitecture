using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Discovers every <see cref="IEndpoint"/> in the given assemblies and maps it. Endpoints are
    /// stateless mappers, so they're instantiated directly via their parameterless constructor.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder endpoints, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(assemblies);

        var endpointTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(type));

        foreach (var type in endpointTypes)
        {
            var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
            endpoint.Map(endpoints);
        }

        return endpoints;
    }
}
