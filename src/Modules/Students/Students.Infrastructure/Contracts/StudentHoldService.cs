using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Students.Contracts;
using Students.Domain;
using Students.Infrastructure.Outbox;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Contracts;

/// <summary>
/// Implements the Students module's <see cref="IStudentHoldService"/> against the Students database.
/// This is the consumer end of the saga's forward leg and, on rejection, the publisher of its reverse
/// leg.
/// <para>
/// A hold is <b>rejected</b> when the student no longer exists or is no longer active (e.g. withdrawn):
/// in that case no hold is recorded and a <see cref="StudentHoldRejected"/> event is enqueued in the
/// Students outbox — in the same transaction — so the Library can compensate. Otherwise the hold is
/// applied. The whole thing is idempotent in the originating message id: an accepted message is marked
/// by the <see cref="StudentHold"/> row, a rejected one by the outbox row (whose id we set to the
/// message id), so a redelivery does neither thing twice.
/// </para>
/// </summary>
internal sealed class StudentHoldService : IStudentHoldService
{
    private readonly StudentsDbContext _db;

    public StudentHoldService(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task PlaceHoldAsync(Guid messageId, Guid studentId, string reason, CancellationToken cancellationToken)
    {
        var alreadyAccepted = await _db.Holds.AnyAsync(hold => hold.Id == messageId, cancellationToken);
        var alreadyRejected = await _db.Outbox.AnyAsync(message => message.Id == messageId, cancellationToken);
        if (alreadyAccepted || alreadyRejected)
        {
            return;
        }

        var status = await _db.Students
            .Where(student => student.Id == studentId)
            .Select(student => (StudentStatus?)student.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null or StudentStatus.Graduated or StudentStatus.Withdrawn)
        {
            // Reject: publish the compensation event back to the Library. The outbox row id is the
            // originating message id, which both makes the rejection idempotent and ties the legs
            // of the saga together.
            var rejectionReason = status is null
                ? "Student no longer exists."
                : $"Student is {status} and cannot be placed on hold.";

            _db.Outbox.Add(new OutboxMessage
            {
                Id = messageId,
                Type = nameof(StudentHoldRejected),
                Content = JsonSerializer.Serialize(new StudentHoldRejected(studentId, rejectionReason)),
                OccurredOnUtc = DateTime.UtcNow,
            });
        }
        else
        {
            _db.Holds.Add(StudentHold.Place(messageId, studentId, reason, DateTime.UtcNow));
        }

        // One transaction: either the hold is recorded, or the rejection event is enqueued.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
