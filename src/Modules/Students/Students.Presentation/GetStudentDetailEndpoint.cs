using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

internal sealed class GetStudentDetailEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/students/{studentId:guid}/detail", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var student = await sender.Send(new GetStudentDetail.Query(studentId), cancellationToken);
            return student is null ? Results.NotFound() : Results.Ok(student);
        })
        .WithName("GetStudentDetail")
        .WithSummary("Student detail — rich projection (address, contacts, enrollments + computed count).");
}
