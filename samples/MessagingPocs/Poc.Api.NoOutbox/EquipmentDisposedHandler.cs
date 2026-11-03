using Messaging;

namespace Poc.Api.NoOutbox;

/// <summary>
/// Runs once per message, in its own DI scope, so it can use scoped services such as a DbContext. Throwing fails the
/// message: the shared queue retries it, then dead-letters it. Delivery is at least once, so real work must tolerate
/// repeats.
/// </summary>
public sealed class EquipmentDisposedHandler : IMessageHandler<EquipmentDisposed>
{
    private readonly ILogger<EquipmentDisposedHandler> logger;

    public EquipmentDisposedHandler(ILogger<EquipmentDisposedHandler> logger)
    {
        this.logger = logger;
    }

    public Task Handle(EquipmentDisposed message, MessageContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Equipment {AssetTag} ({EquipmentId}) disposed by {DisposedBy}",
            message.AssetTag, message.EquipmentId, message.DisposedBy);

        return Task.CompletedTask;
    }
}
