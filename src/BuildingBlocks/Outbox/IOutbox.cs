namespace BuildingBlocks.Outbox;

/// <summary>
/// Stages an integration event for reliable, asynchronous delivery to another module/database. The
/// staged row is written in the <b>same</b> transaction as the business change that produced it (the
/// module's TransactionBehavior commits both at once), so the event can never be lost once the change
/// commits. A background dispatcher later delivers it. The write end of the outbox pattern.
/// </summary>
public interface IOutbox
{
    void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class;
}
