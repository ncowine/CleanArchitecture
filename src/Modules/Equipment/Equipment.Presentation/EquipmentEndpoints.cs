using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Equipment.Presentation;

public static class EquipmentEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var equipment = app.MapGroup("")
            .WithTags("Equipment")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        equipment.MapPost("/equipment", async (
            CreateEquipment.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/equipment/{id}", new { id });
        })
        .WithName("CreateEquipment")
        .WithSummary("Add a piece of hardware to inventory. Pushes an EquipmentCreated event to connected clients.")
        .RequireAuthorization();

        equipment.MapPut("/equipment/{equipmentId:guid}", async (
            Guid equipmentId,
            UpdateEquipmentRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var updated = await sender.Send(
                new UpdateEquipment.Command(equipmentId, request.Name, request.Category, request.AssetTag),
                cancellationToken);
            return updated ? Results.Ok() : Results.NotFound();
        })
        .WithName("UpdateEquipment")
        .WithSummary("Update a piece of equipment. Pushes an EquipmentUpdated event to connected clients.")
        .RequireAuthorization();

        equipment.MapDelete("/equipment/{equipmentId:guid}", async (
            Guid equipmentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var deleted = await sender.Send(new DeleteEquipment.Command(equipmentId), cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteEquipment")
        .WithSummary("Remove a piece of equipment from inventory. Pushes an EquipmentDeleted event to connected clients.")
        .RequireAuthorization();

        equipment.MapGet("/equipment/{equipmentId:guid}", async (
            Guid equipmentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(new GetEquipment.Query(equipmentId), cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetEquipment")
        .WithSummary("A piece of equipment by id. Cache-aside: served from cache when present, else the database (and cached for next time).");

        equipment.MapPost("/equipment/search", async (
            SearchEquipment.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchEquipment")
        .WithSummary("Paged inventory search by category and/or status (paging/filters in the body).");

        equipment.MapGet("/equipment/catalogue", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var catalogue = await sender.Send(new GetEquipmentCatalogue.Query(), cancellationToken);
            return Results.Ok(catalogue);
        })
        .WithName("GetEquipmentCatalogue")
        .WithSummary("The purchasable equipment catalogue, read from a file — no database involved.");

        return app;
    }

    public sealed record UpdateEquipmentRequest(string Name, EquipmentCategory Category, string AssetTag);
}
