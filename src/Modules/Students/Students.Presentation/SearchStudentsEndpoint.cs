using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class SearchStudentsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/students/search", async (
            SearchStudents.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchStudents")
        .WithSummary("Paged student search — paging/filters in the body (POST), returns a PagedResult.");
}
