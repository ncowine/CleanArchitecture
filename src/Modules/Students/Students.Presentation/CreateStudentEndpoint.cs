using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class CreateStudentEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/students", async (
            CreateStudent.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/students/{id}", new { id });
        })
        .WithName("CreateStudent")
        .WithSummary("Enroll a new student.")
        .RequireAuthorization();
}
