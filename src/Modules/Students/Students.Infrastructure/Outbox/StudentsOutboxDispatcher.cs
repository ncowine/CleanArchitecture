using System.Text.Json;
using BuildingBlocks.Outbox;
using Library.Contracts;
using Students.Application.Outbox;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Outbox;

/// <summary>
/// The Students module's outbox dispatch logic. Routes its integration events to the Library module's
/// published contracts: a hold rejection → <see cref="IFineWaiver"/> (the fine-saga's reverse leg), and a
/// withdrawal → <see cref="ILibraryWithdrawalService"/> (return loans, cancel reservations). Plugged into
/// the shared <c>OutboxProcessor&lt;StudentsDbContext&gt;</c>.
/// </summary>
internal sealed class StudentsOutboxDispatcher : IOutboxDispatcher<StudentsDbContext>
{
    private readonly IFineWaiver _fineWaiver;
    private readonly ILibraryWithdrawalService _withdrawal;

    public StudentsOutboxDispatcher(IFineWaiver fineWaiver, ILibraryWithdrawalService withdrawal)
    {
        _fineWaiver = fineWaiver;
        _withdrawal = withdrawal;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case nameof(StudentHoldRejected):
                var rejected = JsonSerializer.Deserialize<StudentHoldRejected>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                return _fineWaiver.WaiveStudentFinesAsync(rejected.StudentId, rejected.Reason, cancellationToken);

            case nameof(StudentWithdrawn):
                var withdrawn = JsonSerializer.Deserialize<StudentWithdrawn>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");

                return _withdrawal.OnStudentWithdrawnAsync(withdrawn.StudentId, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
