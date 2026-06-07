using System.Text.Json;
using BuildingBlocks.Outbox;
using Library.Contracts;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Outbox;

/// <summary>
/// The Students module's outbox dispatch logic (the saga's reverse leg): delivers a hold rejection to
/// the Library module's published <see cref="IFineWaiver"/>. Plugged into the shared
/// <c>OutboxProcessor&lt;StudentsDbContext&gt;</c>.
/// </summary>
internal sealed class StudentsOutboxDispatcher : IOutboxDispatcher<StudentsDbContext>
{
    private readonly IFineWaiver _fineWaiver;

    public StudentsOutboxDispatcher(IFineWaiver fineWaiver)
    {
        _fineWaiver = fineWaiver;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(StudentHoldRejected):
                var @event = JsonSerializer.Deserialize<StudentHoldRejected>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                return _fineWaiver.WaiveStudentFinesAsync(@event.StudentId, @event.Reason, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
