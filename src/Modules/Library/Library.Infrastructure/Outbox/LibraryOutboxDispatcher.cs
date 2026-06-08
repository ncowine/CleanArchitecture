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
    private readonly IStudentBilling _billing;

    public LibraryOutboxDispatcher(IStudentHoldService holds, IStudentBilling billing)
    {
        _holds = holds;
        _billing = billing;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(StudentHoldRequested):
                var hold = JsonSerializer.Deserialize<StudentHoldRequested>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                // The message id is the idempotency key — placing the same hold twice is a no-op.
                return _holds.PlaceHoldAsync(messageId, hold.StudentId, hold.Reason, cancellationToken);

            case nameof(LibraryFineAssessed):
                var fine = JsonSerializer.Deserialize<LibraryFineAssessed>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                // The message id is the idempotency key — the same fine isn't charged twice.
                return _billing.ChargeLibraryFineAsync(messageId, fine.StudentId, fine.Amount, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
