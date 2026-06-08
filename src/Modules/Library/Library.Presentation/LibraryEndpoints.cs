using BuildingBlocks.Messaging;
using Library.Application.Loans;
using Library.Application.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/library/loans", async (
            BorrowBook.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/library/loans/{id}", new { id });
        })
        .WithName("BorrowBook")
        .WithSummary("Lend a book to a student. Writes the Library DB; validates the student against the main DB.")
        .RequireAuthorization();

        app.MapGet("/library/students/{studentId:guid}/loans", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(new GetStudentLoans.Query(studentId), cancellationToken);
            return Results.Ok(response);
        })
        .WithName("GetStudentLoans")
        .WithSummary("Compose a student's loans (Library DB) with their identity (main Students DB).");

        app.MapPost("/library/loans/{loanId:guid}/fines", async (
            Guid loanId,
            AssessFineRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new AssessFine.Command(loanId, request.Amount), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("AssessFine")
        .WithSummary("Assess a fine. Crossing the limit enqueues a hold for the main DB via the outbox.")
        .RequireAuthorization();

        app.MapGet("/library/outbox/dead-letter", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var entries = await sender.Send(new GetDeadLetter.Query(), cancellationToken);
            return Results.Ok(entries);
        })
        .WithName("GetOutboxDeadLetter")
        .WithSummary("List outbox messages that failed past the retry cap and were dead-lettered.");

        app.MapPost("/library/outbox/dead-letter/{messageId:guid}/replay", async (
            Guid messageId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ReplayDeadLetter.Command(messageId), cancellationToken);
            return result.Requeued
                ? Results.Ok(result)
                : Results.NotFound();
        })
        .WithName("ReplayDeadLetter")
        .WithSummary("Requeue a dead-lettered outbox message so the dispatcher attempts delivery again.")
        .RequireAuthorization();

        return app;
    }

    public sealed record AssessFineRequest(decimal Amount);
}
