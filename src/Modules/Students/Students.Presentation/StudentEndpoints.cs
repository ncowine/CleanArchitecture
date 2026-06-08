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
        .WithSummary("Enroll a new student.")
        .RequireAuthorization();

        app.MapGet("/students/{studentId:guid}", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var student = await sender.Send(new GetStudent.Query(studentId), cancellationToken);
            return student is null ? Results.NotFound() : Results.Ok(student);
        })
        .WithName("GetStudent")
        .WithSummary("Student summary — light projection (few fields, no related data).");

        app.MapGet("/students/{studentId:guid}/detail", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var student = await sender.Send(new GetStudentDetail.Query(studentId), cancellationToken);
            return student is null ? Results.NotFound() : Results.Ok(student);
        })
        .WithName("GetStudentDetail")
        .WithSummary("Student detail — rich projection (address, contacts, enrollments + computed count).");

        app.MapPost("/students/search", async (
            SearchStudents.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchStudents")
        .WithSummary("Paged student search — paging/filters in the body (POST), returns a PagedResult.");

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
        .WithSummary("Withdraw a student. A withdrawn student causes later hold requests to be rejected.")
        .RequireAuthorization();

        return app;
    }
}
