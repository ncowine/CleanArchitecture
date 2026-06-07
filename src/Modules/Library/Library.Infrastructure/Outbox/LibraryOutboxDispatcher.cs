using System.Text.Json;
using BuildingBlocks.Outbox;
using Library.Application.Outbox;
using Library.Infrastructure.Persistence;
using Students.Contracts;

namespace Library.Infrastructure.Outbox;

/// <summary>
/// The Library module's outbox dispatch logic: maps its integration events onto the contracts they
/// target. Plugged into the shared <c>OutboxProcessor&lt;LibraryDbContext&gt;</c>.
/// </summary>
internal sealed class LibraryOutboxDispatcher : IOutboxDispatcher<LibraryDbContext>
{
    private readonly IStudentHoldService _holds;

    public LibraryOutboxDispatcher(IStudentHoldService holds)
    {
        _holds = holds;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(StudentHoldRequested):
                var @event = JsonSerializer.Deserialize<StudentHoldRequested>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                // The message id is the idempotency key — placing the same hold twice is a no-op.
                return _holds.PlaceHoldAsync(messageId, @event.StudentId, @event.Reason, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
