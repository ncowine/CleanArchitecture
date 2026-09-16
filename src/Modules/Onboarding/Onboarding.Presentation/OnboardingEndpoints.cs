using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onboarding.Application.Outbox;
using Onboarding.Application.Requests;

namespace Onboarding.Presentation;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var onboarding = app.MapGroup("")
            .WithTags("Onboarding")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var outbox = app.MapGroup("")
            .WithTags("Onboarding — Outbox")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        onboarding.MapPost("/onboarding", async (
            CreateOnboardingRequest.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/onboarding/{id}", new { id });
        })
        .WithName("CreateOnboardingRequest")
        .WithSummary("Register a new hire's onboarding request (what equipment, licence, and access they need).")
        .RequireAuthorization();

        onboarding.MapGet("/onboarding/{onboardingRequestId:guid}", async (
            Guid onboardingRequestId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(new GetOnboardingRequest.Query(onboardingRequestId), cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetOnboardingRequest")
        .WithSummary("The onboarding request as stored — raw status per step, no derived fields.");

        onboarding.MapGet("/onboarding/{onboardingRequestId:guid}/summary", async (
            Guid onboardingRequestId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var response = await sender.Send(new GetOnboardingSummary.Query(onboardingRequestId), cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetOnboardingSummary")
        .WithSummary("Readiness %, Ready/InProgress/AtRisk status, days remaining, missing requirements, blockers, and estimated cost — all computed from the stored request, not stored columns.");

        onboarding.MapPost("/onboarding/{onboardingRequestId:guid}/approve-instant", async (
            Guid onboardingRequestId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ApproveOnboardingInstant.Command(onboardingRequestId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ApproveOnboardingInstant")
        .WithSummary("Run the onboarding saga synchronously in one call: reserve equipment, allocate a licence, provision access — compensating in reverse on the first failure. No crash recovery.")
        .RequireAuthorization();

        onboarding.MapPost("/onboarding/{onboardingRequestId:guid}/approve", async (
            Guid onboardingRequestId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ApproveOnboardingStandard.Command(onboardingRequestId), cancellationToken);
            return Results.Accepted($"/onboarding/{onboardingRequestId}", result);
        })
        .WithName("ApproveOnboardingStandard")
        .WithSummary("Start the onboarding saga asynchronously: enqueues the first step and returns immediately. A background dispatcher drives each step (and compensation, if one fails) — GET the request to watch it progress. Resumes correctly after a restart.")
        .RequireAuthorization();

        outbox.MapPost("/onboarding/outbox/dead-letter/search", async (
            GetDeadLetter.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetOnboardingOutboxDeadLetter")
        .WithSummary("List saga steps that exhausted their delivery attempts and were dead-lettered. Paged in the body (default 1/20, max 100).");

        outbox.MapPost("/onboarding/outbox/dead-letter/{messageId:guid}/replay", async (
            Guid messageId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ReplayDeadLetter.Command(messageId), cancellationToken);
            return result.Requeued ? Results.Ok(result) : Results.NotFound();
        })
        .WithName("ReplayOnboardingDeadLetter")
        .WithSummary("Requeue a dead-lettered saga step so the dispatcher attempts it again.")
        .RequireAuthorization();

        return app;
    }
}
