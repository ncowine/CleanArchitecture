using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TesterGuide.Application.Actions;
using TesterGuide.Application.Configs;
using TesterGuide.Application.Content;
using TesterGuide.Application.Focuses;
using TesterGuide.Application.Outbox;
using TesterGuide.Application.Templates;
using TesterGuide.Domain;

namespace TesterGuide.Presentation;

public static class TesterGuideEndpoints
{
    public static IEndpointRouteBuilder MapTesterGuideEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        // Every route lives under /guide; each group carries the prefix so the individual maps below stay
        // relative (a group per Swagger tag, same shape as the other modules).
        var focuses = MapGuideGroup(app, versionSet, "Tester Guide — Focus Manager");
        var templates = MapGuideGroup(app, versionSet, "Tester Guide — Templates");
        var configs = MapGuideGroup(app, versionSet, "Tester Guide — Configs");
        var content = MapGuideGroup(app, versionSet, "Tester Guide — Content Manager");
        var actions = MapGuideGroup(app, versionSet, "Tester Guide — Actions");
        var outbox = MapGuideGroup(app, versionSet, "Tester Guide — Outbox");

        // ---- Focus manager ------------------------------------------------------------------------------

        focuses.MapPost("/focuses", async (
            CreateFocus.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/guide/focuses/{id}", new { id });
        })
        .WithName("CreateFocus")
        .WithSummary("Create a focus (a named label attached to configs).")
        .RequireAuthorization();

        focuses.MapPut("/focuses/{focusId:guid}", async (
            Guid focusId, FocusRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new UpdateFocus.Command(focusId, request.Name, request.Description), cancellationToken);
            return Results.Ok(new { id = focusId });
        })
        .WithName("UpdateFocus")
        .WithSummary("Rename a focus / change its description.")
        .RequireAuthorization();

        focuses.MapDelete("/focuses/{focusId:guid}", async (
            Guid focusId, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteFocus.Command(focusId), cancellationToken);
            return Results.NoContent();
        })
        .WithName("DeleteFocus")
        .WithSummary("Delete a focus.")
        .RequireAuthorization();

        focuses.MapGet("/focuses", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListFocuses.Query(), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListFocuses")
        .WithSummary("List all focuses.");

        // ---- Templates ----------------------------------------------------------------------------------

        templates.MapPost("/templates", async (
            CreateConfigTemplate.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/guide/templates/{id}", new { id });
        })
        .WithName("CreateConfigTemplate")
        .WithSummary("Create a config template (reusable defaults that speed up config creation).")
        .RequireAuthorization();

        templates.MapGet("/templates", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListConfigTemplates.Query(), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListConfigTemplates")
        .WithSummary("List all config templates.");

        // ---- Configs ------------------------------------------------------------------------------------

        configs.MapPost("/configs", async (
            CreateGuideConfig.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/guide/configs/{id}", new { id });
        })
        .WithName("CreateGuideConfig")
        .WithSummary("Create a config dedicated to a test plan + version (validates them in the primary DB via the catalog contract).")
        .RequireAuthorization();

        configs.MapPost("/configs/from-template", async (
            CreateConfigFromTemplate.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/guide/configs/{id}", new { id });
        })
        .WithName("CreateConfigFromTemplate")
        .WithSummary("Create a config from a template (template supplies focus/mode/sync).")
        .RequireAuthorization();

        configs.MapPost("/configs/search", async (
            SearchGuideConfigs.Query query, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchGuideConfigs")
        .WithSummary("Paged search of configs, optionally filtered by test plan (paging/filters in the body).");

        configs.MapGet("/configs/{configId:guid}", async (
            Guid configId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetGuideConfig.Query(configId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetGuideConfig")
        .WithSummary("Render a config: its own data composed with the primary system's content tree and version label.");

        configs.MapPost("/configs/{configId:guid}/assignments", async (
            Guid configId, AssignUserRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new AssignUser.Command(configId, request.UserId, request.DisplayName, request.Role), cancellationToken);
            return Results.Created($"/guide/configs/{configId}/assignments/{id}", new { id });
        })
        .WithName("AssignUser")
        .WithSummary("Assign a user (an authenticated principal) to a config.")
        .RequireAuthorization();

        // ---- Content manager ----------------------------------------------------------------------------

        content.MapPost("/content", async (
            SetTaskContentEnabled.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SetTaskContentEnabled")
        .WithSummary("Content manager: enable/disable a primary-system task for the tool (overlay, keyed by primary ids).")
        .RequireAuthorization();

        content.MapGet("/content/{testPlanId:guid}", async (
            Guid testPlanId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListContentSelections.Query(testPlanId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListContentSelections")
        .WithSummary("List the content-manager overlay for a test plan (which tasks are enabled for the tool).");

        // ---- Actions (record + sync saga) ---------------------------------------------------------------

        actions.MapPost("/configs/{configId:guid}/actions", async (
            Guid configId, RecordActionRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new RecordAction.Command(configId, request.TestTaskId, request.PlatformId, request.Status),
                cancellationToken);
            return Results.Ok(result);
        })
        .WithName("RecordAction")
        .WithSummary("Record a tester's action against a task. When the config has sync enabled, it mirrors into the primary action log via the outbox (the sync saga's forward leg).")
        .RequireAuthorization();

        actions.MapGet("/configs/{configId:guid}/actions", async (
            Guid configId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListConfigActions.Query(configId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ListConfigActions")
        .WithSummary("A config's action log, newest first, with each action's sync state (and any rejection reason).");

        actions.MapGet("/configs/{configId:guid}/live", (Guid configId, IPresenceTracker presence) =>
            Results.Ok(new { configId, users = presence.UsersIn(RealtimeGroups.Config(configId)) }))
        .WithName("GetConfigLiveView")
        .WithSummary("Live view snapshot: who is currently connected to this config (the push feed is the /hubs/presence SignalR hub).");

        // ---- Outbox admin -------------------------------------------------------------------------------

        outbox.MapGet("/outbox/dead-letter", async (
            int? page, int? pageSize, ISender sender, CancellationToken cancellationToken) =>
        {
            var entries = await sender.Send(new GetDeadLetter.Query(page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(entries);
        })
        .WithName("GetGuideOutboxDeadLetter")
        .WithSummary("List Tester Guide outbox messages that failed past the retry cap and were dead-lettered. Paged via ?page=&pageSize= (default 1/20, max 100).");

        outbox.MapPost("/outbox/dead-letter/{messageId:guid}/replay", async (
            Guid messageId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ReplayDeadLetter.Command(messageId), cancellationToken);
            return result.Requeued ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("ReplayGuideDeadLetter")
        .WithSummary("Requeue a dead-lettered Tester Guide outbox message so the dispatcher attempts delivery again.")
        .RequireAuthorization();

        return app;
    }

    // A /guide route group for one Swagger tag, on v1 of the shared version set.
    private static RouteGroupBuilder MapGuideGroup(IEndpointRouteBuilder app, ApiVersionSet versionSet, string tag) =>
        app.MapGroup("/guide")
            .WithTags(tag)
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

    public sealed record FocusRequest(string Name, string? Description);

    public sealed record AssignUserRequest(string UserId, string DisplayName, ConfigRole Role);

    public sealed record RecordActionRequest(Guid TestTaskId, Guid PlatformId, ActionStatus Status);
}
