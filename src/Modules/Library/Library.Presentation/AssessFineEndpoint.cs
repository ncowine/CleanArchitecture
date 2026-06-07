using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Library.Application.Loans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

internal sealed class AssessFineEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/library/loans/{loanId:guid}/fines", async (
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
}

public sealed record AssessFineRequest(decimal Amount);
