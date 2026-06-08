using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Library.Application.Catalog;
using Library.Application.Loans;
using Library.Application.Outbox;
using Library.Application.Reservations;
using Library.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var loans = app.MapGroup("")
            .WithTags("Library — Loans")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var outbox = app.MapGroup("")
            .WithTags("Library — Outbox")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var catalog = app.MapGroup("")
            .WithTags("Library — Catalog")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var reservations = app.MapGroup("")
            .WithTags("Library — Reservations")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        loans.MapPost("/library/loans", async (
            BorrowBook.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/library/loans/{id}", new { id });
        })
        .WithName("BorrowBook")
        .WithSummary("Lend an available copy to a student (by copy id). Validates the student in the main DB, enforces the borrow limit, and sets the due date by policy.")
        .RequireAuthorization();

        loans.MapGet("/library/students/{studentId:guid}/loans", async (
            Guid studentId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(
                new GetStudentLoans.Query(studentId, page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(response);
        })
        .WithName("GetStudentLoans")
        .WithSummary("Compose a student's identity (main Students DB) with a page of their loans (Library DB). Paged via ?page=&pageSize= (default 1/20, max 100).");

        loans.MapPost("/library/loans/{loanId:guid}/fines", async (
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

        loans.MapPost("/library/copies/{copyId:guid}/return", async (
            Guid copyId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ReturnLoan.Command(copyId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ReturnLoan")
        .WithSummary("Return a borrowed copy: closes the loan (with any overdue fine) and frees the copy.")
        .RequireAuthorization();

        loans.MapPost("/library/copies/{copyId:guid}/renew", async (
            Guid copyId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RenewLoan.Command(copyId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("RenewLoan")
        .WithSummary("Renew the active loan on a copy, extending its due date up to the renewal limit.")
        .RequireAuthorization();

        outbox.MapGet("/library/outbox/dead-letter", async (
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var entries = await sender.Send(
                new GetDeadLetter.Query(page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(entries);
        })
        .WithName("GetOutboxDeadLetter")
        .WithSummary("List outbox messages that failed past the retry cap and were dead-lettered. Paged via ?page=&pageSize= (default 1/20, max 100).");

        outbox.MapPost("/library/outbox/dead-letter/{messageId:guid}/replay", async (
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

        // ---- Catalog & inventory --------------------------------------------------------------------

        catalog.MapPost("/library/books", async (
            AddBook.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/library/books/{id}", new { id });
        })
        .WithName("AddBook")
        .WithSummary("Add a title to the catalog.")
        .RequireAuthorization();

        catalog.MapGet("/library/books/{bookId:guid}", async (
            Guid bookId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var book = await sender.Send(new GetBook.Query(bookId), cancellationToken);
            return book is null ? Results.NotFound() : Results.Ok(book);
        })
        .WithName("GetBook")
        .WithSummary("A catalog title with live total/available copy counts.");

        catalog.MapPost("/library/books/search", async (
            SearchBooks.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchBooks")
        .WithSummary("Paged catalog search by category and/or title/author text (paging/filters in the body).");

        catalog.MapPost("/library/books/{bookId:guid}/copies", async (
            Guid bookId,
            AddCopyRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new AddCopy.Command(bookId, request.Barcode, request.Condition), cancellationToken);
            return Results.Created($"/library/copies/{id}", new { id });
        })
        .WithName("AddCopy")
        .WithSummary("Add a physical copy of a title to the collection (starts available).")
        .RequireAuthorization();

        catalog.MapGet("/library/books/{bookId:guid}/copies", async (
            Guid bookId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var copies = await sender.Send(
                new GetBookCopies.Query(bookId, page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(copies);
        })
        .WithName("GetBookCopies")
        .WithSummary("Paged list of a book's copies with condition and status. Paged via ?page=&pageSize= (default 1/20, max 100).");

        catalog.MapPost("/library/copies/{copyId:guid}/withdraw", async (
            Guid copyId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new WithdrawCopy.Command(copyId), cancellationToken);
            return Results.Ok(new { copyId = id });
        })
        .WithName("WithdrawCopy")
        .WithSummary("Withdraw a copy from the collection (not allowed while it is on loan).")
        .RequireAuthorization();

        catalog.MapPost("/library/copies/{copyId:guid}/lost", async (
            Guid copyId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new MarkCopyLost.Command(copyId), cancellationToken);
            return Results.Ok(new { copyId = id });
        })
        .WithName("MarkCopyLost")
        .WithSummary("Mark a copy as lost.")
        .RequireAuthorization();

        // ---- Reservations (hold queue) --------------------------------------------------------------

        reservations.MapPost("/library/books/{bookId:guid}/reservations", async (
            Guid bookId,
            ReserveRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ReserveBook.Command(request.StudentId, bookId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ReserveBook")
        .WithSummary("Place a hold on a book (only when no copy is available); joins the end of the queue.")
        .RequireAuthorization();

        reservations.MapGet("/library/books/{bookId:guid}/reservations", async (
            Guid bookId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var queue = await sender.Send(
                new GetBookReservations.Query(bookId, page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(queue);
        })
        .WithName("GetBookReservations")
        .WithSummary("The active hold queue for a book, in order. Paged via ?page=&pageSize= (default 1/20, max 100).");

        reservations.MapGet("/library/students/{studentId:guid}/reservations", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetStudentReservations.Query(studentId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetStudentReservations")
        .WithSummary("A student's active reservations (with book titles).");

        reservations.MapPost("/library/reservations/{reservationId:guid}/cancel", async (
            Guid reservationId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new CancelReservation.Command(reservationId), cancellationToken);
            return Results.Ok(new { reservationId = id });
        })
        .WithName("CancelReservation")
        .WithSummary("Cancel a reservation; releases any held copy to the next person in line.")
        .RequireAuthorization();

        reservations.MapPost("/library/reservations/{reservationId:guid}/fulfill", async (
            Guid reservationId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new FulfillReservation.Command(reservationId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("FulfillReservation")
        .WithSummary("Pick up a ready reservation: issues a loan for the held copy.")
        .RequireAuthorization();

        reservations.MapPost("/library/reservations/{reservationId:guid}/expire", async (
            Guid reservationId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new ExpireReservation.Command(reservationId), cancellationToken);
            return Results.Ok(new { reservationId = id });
        })
        .WithName("ExpireReservation")
        .WithSummary("Expire a held reservation past its pickup window; releases the copy to the next in line.")
        .RequireAuthorization();

        return app;
    }

    public sealed record AssessFineRequest(decimal Amount);

    public sealed record AddCopyRequest(string Barcode, CopyCondition Condition);

    public sealed record ReserveRequest(Guid StudentId);
}
