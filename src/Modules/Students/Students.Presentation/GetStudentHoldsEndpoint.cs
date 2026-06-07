using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class GetStudentHoldsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/students/{studentId:guid}/holds", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var holds = await sender.Send(new GetStudentHolds.Query(studentId), cancellationToken);
            return Results.Ok(holds);
        })
        .WithName("GetStudentHolds")
        .WithSummary("List holds on a student (where cross-module outbox writes-back land).");
}
