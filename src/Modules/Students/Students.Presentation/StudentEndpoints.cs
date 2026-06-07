using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Students;

namespace Students.Presentation;

public static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/students", async (
            CreateStudent.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/students/{id}", new { id });
        })
        .WithName("CreateStudent")
        .WithSummary("Enroll a new student.");

        app.MapGet("/students/{studentId:guid}/holds", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var holds = await sender.Send(new GetStudentHolds.Query(studentId), cancellationToken);
            return Results.Ok(holds);
        })
        .WithName("GetStudentHolds")
        .WithSummary("List holds on a student (where cross-module outbox writes-back land).");

        app.MapPost("/students/{studentId:guid}/withdraw", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new WithdrawStudent.Command(studentId), cancellationToken);
            return Results.Ok(new { id, status = "Withdrawn" });
        })
        .WithName("WithdrawStudent")
        .WithSummary("Withdraw a student. A withdrawn student causes later hold requests to be rejected.");

        return app;
    }
}
