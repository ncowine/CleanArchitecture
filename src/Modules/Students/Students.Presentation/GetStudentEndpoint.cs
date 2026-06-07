using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class GetStudentEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/students/{studentId:guid}", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var student = await sender.Send(new GetStudent.Query(studentId), cancellationToken);
            return student is null ? Results.NotFound() : Results.Ok(student);
        })
        .WithName("GetStudent")
        .WithSummary("Student summary — light projection (few fields, no related data).");
}
