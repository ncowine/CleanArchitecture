using BuildingBlocks.Messaging;
using Equipment.Application;
using Equipment.Infrastructure.IntegrationEvents;
using Messaging;

namespace Equipment.Infrastructure.Behaviors;

/// <summary>
/// Publishes the events a command collected in <see cref="EquipmentEvents"/> once it has succeeded. Registered outside
/// <see cref="TransactionBehavior{TRequest,TResponse}"/>, so that means after the commit: if the command throws or the
/// commit fails, nothing is published.
/// </summary>
/// <remarks>
/// <see cref="IMessagePublisher"/> only buffers, so publishing never fails the request. Without messaging configured
/// there is no publisher and the events are dropped.
/// </remarks>
internal sealed class EquipmentEventsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IEquipmentCommand
{
    private readonly EquipmentEvents _events;
    private readonly IMessagePublisher? _publisher;

    public EquipmentEventsBehavior(EquipmentEvents events, IMessagePublisher? publisher = null)
    {
        _events = events;
        _publisher = publisher;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        foreach (var integrationEvent in _events.Drain())
        {
            _publisher?.Publish(integrationEvent);
        }

        return response;
    }
}
