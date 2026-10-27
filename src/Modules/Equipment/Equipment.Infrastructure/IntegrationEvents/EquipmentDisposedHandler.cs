using BuildingBlocks.Messaging;
using Equipment.Application.Inventory;
using Messaging;
using Microsoft.Extensions.Logging;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// Removes disposed equipment from inventory. Goes through the same <see cref="DeleteEquipment"/> command as
/// <c>DELETE /equipment/{id}</c>, so validation, the transaction, auditing (actor "system") and the outgoing
/// <c>EquipmentDeleted</c> event are exactly the same as for an HTTP delete.
/// </summary>
/// <remarks>
/// Delivery is at least once. A repeat is harmless: deleting equipment that's already gone returns false and changes
/// nothing. Throwing fails the message, which the API's shared queue retries and then dead-letters.
/// </remarks>
public sealed partial class EquipmentDisposedHandler : IMessageHandler<EquipmentDisposed>
{
    private readonly ISender _sender;
    private readonly ILogger<EquipmentDisposedHandler> _logger;

    public EquipmentDisposedHandler(ISender sender, ILogger<EquipmentDisposedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(EquipmentDisposed message, MessageContext context, CancellationToken cancellationToken)
    {
        bool deleted = await _sender.Send(new DeleteEquipment.Command(message.EquipmentId), cancellationToken);
        LogHandled(message.AssetTag, message.EquipmentId, message.DisposedBy, deleted);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Equipment {AssetTag} ({EquipmentId}) disposed by {DisposedBy}; removed from inventory: {Deleted}")]
    private partial void LogHandled(string assetTag, Guid equipmentId, string disposedBy, bool deleted);
}
