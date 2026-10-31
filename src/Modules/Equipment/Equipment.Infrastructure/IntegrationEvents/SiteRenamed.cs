using Messaging;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// A site was renamed by the system that owns sites (they are read-only reference data here). Received from RabbitMQ,
/// never sent.
/// </summary>
/// <remarks>
/// Nothing publishes this yet. When the owner does, add its exchange to <c>Messaging:Buses:Main:Subscriptions</c> and
/// make sure the wire name below is the one it sends (a legacy Prism event's full type name, if it's a legacy app).
/// </remarks>
[Message("ReferenceData.SiteRenamed")]
public sealed class SiteRenamed
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = "";
}
