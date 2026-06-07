using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Library.Application.Loans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

internal sealed class BorrowBookEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/library/loans", async (
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
}
