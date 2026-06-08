namespace Students.Application.Abstractions;

/// <summary>
/// Enqueues an integration event in the Students module's outbox (committed by the same unit of work as
/// the business change). Module-specific on purpose: the shared <c>IOutbox</c> is an open-generic single
/// registration owned by the Library module, so each module that also publishes gets its own writer.
/// </summary>
public interface IStudentOutbox
{
    void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class;
}
