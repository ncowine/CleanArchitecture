namespace Equipment.Application.Abstractions;

/// <summary>
/// Integration events (from <c>Equipment.Messages</c>) for other applications. Collected while the command runs and
/// published once its transaction has committed, so a change that rolls back is never announced.
/// </summary>
/// <remarks>
/// Best effort, like the real-time events: an event can be lost if the process stops before the bus sends it. When the
/// host hasn't configured messaging, events are dropped. The handler only says "this happened"; the host decides the
/// transport.
/// </remarks>
public interface IEquipmentEvents
{
    void Publish<TEvent>(TEvent integrationEvent) where TEvent : class;
}
