using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Library.Application.Outbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Library.Presentation;

internal sealed class GetOutboxDeadLetterEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/library/outbox/dead-letter", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var entries = await sender.Send(new GetDeadLetter.Query(), cancellationToken);
            return Results.Ok(entries);
        })
        .WithName("GetOutboxDeadLetter")
        .WithSummary("List outbox messages that failed past the retry cap and were dead-lettered.");
}
