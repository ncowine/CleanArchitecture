using SharedKernel.Models;

namespace SharedKernel.DataService;

/// <summary>
/// Read-only access to shared reference data. Consumed both by the standalone reference endpoint
/// (SharedKernel.Presentation) and directly by modules that need to enrich their own response DTOs with
/// a field from here — a plain project reference, same tier as a BuildingBlocks dependency, not a
/// module-to-module call.
/// </summary>
public interface IReferenceDataService
{
    Task<IReadOnlyList<Site>> GetSitesAsync(CancellationToken cancellationToken);

    Task<Site?> GetSiteAsync(Guid siteId, CancellationToken cancellationToken);
}
