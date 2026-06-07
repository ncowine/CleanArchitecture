using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Library.Application.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

internal sealed class ReplayDeadLetterEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/library/outbox/dead-letter/{messageId:guid}/replay", async (
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
}
