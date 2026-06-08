namespace CleanArch.DesktopClient.Api;

// Client-side DTOs mirroring the API's JSON responses (the client never references the server projects).

public sealed record StudentSummary(Guid Id, string FullName, string Email, string Status);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record AddressDto(string Line1, string? Line2, string City, string State, string PostalCode, string Country);

public sealed record EmergencyContactDto(string Name, string Relationship, string PhoneNumber);

public sealed record EnrollmentDto(Guid ProgramId, string Term, string Status, DateOnly EnrolledOn, string? Grade);

public sealed record StudentDetail(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    AddressDto? Address,
    IReadOnlyList<EmergencyContactDto> EmergencyContacts,
    IReadOnlyList<EnrollmentDto> Enrollments,
    int ActiveEnrollments);

public sealed record LoanSummary(
    Guid Id,
    string BookTitle,
    DateOnly BorrowedOn,
    DateOnly DueOn,
    DateOnly? ReturnedOn,
    decimal FineAmount,
    int RenewalCount);

public sealed record StudentLoans(Guid StudentId, string StudentName, string Status, PagedResult<LoanSummary> Loans);

public sealed record AssessFineResult(decimal TotalFines, bool HoldRequested);
