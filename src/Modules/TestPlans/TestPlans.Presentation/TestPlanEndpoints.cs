using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TestPlans.Application.Authoring;
using TestPlans.Application.Reads;
using TestPlans.Application.Results;
using TestPlans.Domain;

namespace TestPlans.Presentation;

public static class TestPlanEndpoints
{
    public static IEndpointRouteBuilder MapTestPlanEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var authoring = app.MapGroup("")
            .WithTags("Test Plans — Authoring")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var reads = app.MapGroup("")
            .WithTags("Test Plans — Content")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        // ---- Authoring (the stand-in system of record) --------------------------------------------------

        authoring.MapPost("/testplans", async (
            CreateTestPlan.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/testplans/{id}", new { id });
        })
        .WithName("CreateTestPlan")
        .WithSummary("Create a test plan (the root of a content tree).")
        .RequireAuthorization();

        authoring.MapPost("/testplans/{testPlanId:guid}/categories", async (
            Guid testPlanId, CategoryRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new AddCategory.Command(testPlanId, request.Name, request.Order), cancellationToken);
            return Results.Created($"/testplans/categories/{id}", new { id });
        })
        .WithName("AddCategory")
        .WithSummary("Add a category to a test plan.")
        .RequireAuthorization();

        authoring.MapPost("/testplans/categories/{categoryId:guid}/subcategories", async (
            Guid categoryId, SubCategoryRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new AddSubCategory.Command(categoryId, request.Name, request.Order), cancellationToken);
            return Results.Created($"/testplans/subcategories/{id}", new { id });
        })
        .WithName("AddSubCategory")
        .WithSummary("Add a sub-category to a category.")
        .RequireAuthorization();

        authoring.MapPost("/testplans/subcategories/{subCategoryId:guid}/tasks", async (
            Guid subCategoryId, TaskRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new AddTask.Command(subCategoryId, request.Name, request.Description, request.Mode), cancellationToken);
            return Results.Created($"/testplans/tasks/{id}", new { id });
        })
        .WithName("AddTask")
        .WithSummary("Add a task to a sub-category.")
        .RequireAuthorization();

        authoring.MapPost("/testplans/{testPlanId:guid}/versions", async (
            Guid testPlanId, VersionRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new AddVersion.Command(testPlanId, request.Version, request.SubVersion), cancellationToken);
            return Results.Created($"/testplans/versions/{id}", new { id });
        })
        .WithName("AddVersion")
        .WithSummary("Add a (Version.SubVersion) to a test plan.")
        .RequireAuthorization();

        authoring.MapPost("/testplans/platforms", async (
            AddPlatform.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/testplans/platforms/{id}", new { id });
        })
        .WithName("AddPlatform")
        .WithSummary("Add a platform (variation) to the shared reference list.")
        .RequireAuthorization();

        authoring.MapPost("/testplans/tasks/{taskId:guid}/results", async (
            Guid taskId, RecordResultRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new RecordResult.Command(taskId, request.PlatformId, request.TestPlanVersionId, request.Status),
                cancellationToken);
            return Results.Ok(new { actionId = id });
        })
        .WithName("RecordResult")
        .WithSummary("Record a result against a task natively in the primary system (Source = Primary).")
        .RequireAuthorization();

        // ---- Content reads ------------------------------------------------------------------------------

        reads.MapGet("/testplans/{testPlanId:guid}/tree", async (
            Guid testPlanId, ISender sender, CancellationToken cancellationToken) =>
        {
            var tree = await sender.Send(new GetTestPlanTree.Query(testPlanId), cancellationToken);
            return tree is null ? Results.NotFound() : Results.Ok(tree);
        })
        .WithName("GetTestPlanTree")
        .WithSummary("The full content tree (categories → sub-categories → tasks) of a test plan.");

        reads.MapGet("/testplans/{testPlanId:guid}/versions", async (
            Guid testPlanId, ISender sender, CancellationToken cancellationToken) =>
        {
            var versions = await sender.Send(new ListVersions.Query(testPlanId), cancellationToken);
            return Results.Ok(versions);
        })
        .WithName("ListVersions")
        .WithSummary("A test plan's versions, ordered by Version then SubVersion.");

        reads.MapGet("/testplans/platforms", async (
            ISender sender, CancellationToken cancellationToken) =>
        {
            var platforms = await sender.Send(new ListPlatforms.Query(), cancellationToken);
            return Results.Ok(platforms);
        })
        .WithName("ListPlatforms")
        .WithSummary("The shared platforms (variations).");

        reads.MapGet("/testplans/tasks/{taskId:guid}/status", async (
            Guid taskId, Guid platformId, Guid versionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var status = await sender.Send(new GetTaskStatus.Query(taskId, platformId, versionId), cancellationToken);
            return status is null ? Results.NotFound() : Results.Ok(status);
        })
        .WithName("GetTaskStatus")
        .WithSummary("A task's current status for a (platform, version) from the primary source of truth. Pass ?platformId=&versionId=.");

        return app;
    }

    public sealed record CategoryRequest(string Name, int Order);

    public sealed record SubCategoryRequest(string Name, int Order);

    public sealed record TaskRequest(string Name, string? Description, TaskMode Mode);

    public sealed record VersionRequest(int Version, int SubVersion);

    public sealed record RecordResultRequest(Guid PlatformId, Guid TestPlanVersionId, TaskResultStatus Status);
}
