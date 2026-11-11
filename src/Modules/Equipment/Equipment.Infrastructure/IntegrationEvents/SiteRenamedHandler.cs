using Equipment.Infrastructure.Caching;
using Messaging;
using Microsoft.Extensions.Logging;
using SharedKernel.DataService;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// Refreshes the site's equipment summary, which shows the site's name. The cached site comes first: the summary's
/// refetch reads the name through that cache, so in the other order it would cache the old name again.
/// </summary>
/// <remarks>
/// A repeat is harmless: it just refreshes again. On a shared queue only one API instance gets the message, so with
/// several instances the others keep the old name until their caches expire.
/// </remarks>
public sealed partial class SiteRenamedHandler : IMessageHandler<SiteRenamed>
{
    private readonly IReferenceDataCache _referenceData;
    private readonly SiteEquipmentSummaryCache _summaries;
    private readonly ILogger<SiteRenamedHandler> _logger;

    public SiteRenamedHandler(IReferenceDataCache referenceData, SiteEquipmentSummaryCache summaries, ILogger<SiteRenamedHandler> logger)
    {
        _referenceData = referenceData;
        _summaries = summaries;
        _logger = logger;
    }

    public async Task HandleAsync(SiteRenamed message, MessageContext context, CancellationToken cancellationToken)
    {
        await _referenceData.ForgetSiteAsync(message.SiteId, cancellationToken);
        _summaries.OnSiteChanged(message.SiteId);
        LogHandled(message.SiteId, message.Name);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Site {SiteId} renamed to {Name}; refreshing its equipment summary")]
    private partial void LogHandled(Guid siteId, string name);
}
