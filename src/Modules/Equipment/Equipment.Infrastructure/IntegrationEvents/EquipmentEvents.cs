using Equipment.Application.Abstractions;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// Scoped, per-request collector of Equipment's integration events. <see cref="Behaviors.EquipmentEventsBehavior{TRequest,TResponse}"/>
/// drains it after the commit.
/// </summary>
internal sealed class EquipmentEvents : IEquipmentEvents
{
    private readonly List<object> _pending = new();

    public void Publish<TEvent>(TEvent integrationEvent) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _pending.Add(integrationEvent);
    }

    /// <summary>Returns and clears the pending events.</summary>
    public IReadOnlyList<object> Drain()
    {
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }
}
