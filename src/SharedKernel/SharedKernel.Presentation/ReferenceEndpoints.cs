using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel.DataService;

namespace SharedKernel.Presentation;

/// <summary>
/// Read-only reference data shared across modules — not a bounded context of its own (no module owns
/// it), so unlike the module endpoint groups there's no command side here, just GETs.
/// </summary>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var reference = app.MapGroup("/reference")
            .WithTags("Reference")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        reference.MapGet("/sites", async (
            IReferenceDataService referenceData,
            CancellationToken cancellationToken) =>
        {
            var sites = await referenceData.GetSitesAsync(cancellationToken);
            return Results.Ok(sites.Select(site => new SiteResponse(site.Id, site.Code, site.Name, site.City)));
        })
        .WithName("GetSites")
        .WithSummary("Company office sites. Long-lived cache (24h) — an out-of-band data change is picked up on expiry or the next app restart, whichever comes first.");

        reference.MapGet("/sites/{siteId:guid}", async (
            Guid siteId,
            IReferenceDataService referenceData,
            CancellationToken cancellationToken) =>
        {
            var site = await referenceData.GetSiteAsync(siteId, cancellationToken);
            return site is null
                ? Results.NotFound()
                : Results.Ok(new SiteResponse(site.Id, site.Code, site.Name, site.City));
        })
        .WithName("GetSite")
        .WithSummary("A single company office site by id.");

        return app;
    }
}
