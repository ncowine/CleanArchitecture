using BuildingBlocks.Messaging;
using Students.Application.Abstractions;

namespace Students.Application.Students;

/// <summary>
/// Rich read — a student plus related data (address, emergency contacts, enrollments) and a computed
/// count. Same entity as <see cref="GetStudent"/>, different shape: its own response record, and the
/// read service Includes/aggregates only on this path. Nested DTOs are reusable building blocks, not
/// optional flags on one god-object.
/// </summary>
public static class GetStudentDetail
{
    public sealed record Query(Guid StudentId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        string Status,
        AddressDto? Address,
        IReadOnlyList<EmergencyContactDto> EmergencyContacts,
        IReadOnlyList<EnrollmentDto> Enrollments,
        int ActiveEnrollments);

    public sealed record AddressDto(
        string Line1, string? Line2, string City, string State, string PostalCode, string Country);

    public sealed record EmergencyContactDto(string Name, string Relationship, string PhoneNumber);

    public sealed record EnrollmentDto(
        Guid ProgramId, string Term, string Status, DateOnly EnrolledOn, string? Grade);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IStudentReadService _reads;

        public Handler(IStudentReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetDetailAsync(query.StudentId, cancellationToken);
    }
}
