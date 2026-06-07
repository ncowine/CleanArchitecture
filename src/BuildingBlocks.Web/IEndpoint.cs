using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web;

/// <summary>
/// A self-registering endpoint: each feature owns its route in its own file by implementing this. The
/// host discovers and maps them all via <c>MapEndpoints</c> — no central endpoints file to edit.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder endpoints);
}
