namespace SharedKernel.DataService;

/// <summary>
/// Forgets cached reference data when the system that owns it says it changed (e.g. a <c>SiteRenamed</c> message).
/// Reference data has no write path through this app, so this is the only way to refresh it before its 24-hour expiry.
/// </summary>
public interface IReferenceDataCache
{
    /// <summary>Removes <paramref name="siteId"/> and the site list, so the next read goes to the database.</summary>
    Task ForgetSiteAsync(Guid siteId, CancellationToken cancellationToken);
}
