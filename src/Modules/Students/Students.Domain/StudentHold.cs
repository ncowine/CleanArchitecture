namespace Students.Domain;

/// <summary>
/// A hold placed on a student in the main Students database, requested by another module (e.g.
/// Library when fines pile up). <see cref="Id"/> is deliberately the id of the originating outbox
/// message, not a fresh guid — that makes applying the same message twice a no-op, which is what
/// keeps the cross-database outbox delivery idempotent (at-least-once becomes effectively-once).
/// </summary>
public sealed class StudentHold
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTime PlacedOnUtc { get; private set; }

    private StudentHold() { }

    public static StudentHold Place(Guid id, Guid studentId, string reason, DateTime placedOnUtc)
    {
        if (id == Guid.Empty)
            throw new DomainException("A hold must carry the originating message id.");
        if (studentId == Guid.Empty)
            throw new DomainException("A hold must reference a student.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A hold must have a reason.");

        return new StudentHold
        {
            Id = id,
            StudentId = studentId,
            Reason = reason.Trim(),
            PlacedOnUtc = placedOnUtc,
        };
    }
}
