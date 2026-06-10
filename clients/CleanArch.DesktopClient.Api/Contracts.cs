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

public sealed record StudentHold(Guid Id, string Reason, DateTime PlacedOnUtc);

public sealed record LoanSummary(
    Guid Id,
    Guid CopyId,
    string BookTitle,
    DateOnly BorrowedOn,
    DateOnly DueOn,
    DateOnly? ReturnedOn,
    decimal FineAmount,
    int RenewalCount)
{
    public bool IsActive => ReturnedOn is null;
}

public sealed record StudentLoans(Guid StudentId, string StudentName, string Status, PagedResult<LoanSummary> Loans);

public sealed record AssessFineResult(decimal TotalFines, bool HoldRequested);

public sealed record ReturnLoanResult(Guid LoanId, bool WasOverdue, decimal OverdueFine, decimal TotalFine, bool HeldForReservation);

public sealed record RenewLoanResult(Guid LoanId, DateOnly DueOn, int RenewalCount);

// ---- Catalog -----------------------------------------------------------------------------------

public sealed record BookListItem(Guid Id, string Isbn, string Title, string Author, string Category, int AvailableCopies);

public sealed record CopyListItem(Guid Id, string Barcode, string Condition, string Status, DateOnly AcquiredOn)
{
    public bool IsAvailable => Status == "Available";
    public bool IsOnLoan => Status == "OnLoan";
}

// ---- Reservations ------------------------------------------------------------------------------

public sealed record ReserveResult(Guid ReservationId, int QueuePosition);

public sealed record StudentReservation(
    Guid ReservationId,
    Guid BookId,
    string BookTitle,
    string Status,
    int? QueuePosition,
    DateOnly? ExpiresOn);

// ---- Billing -----------------------------------------------------------------------------------

public sealed record AccountEntry(Guid Id, string Kind, string? Category, decimal Amount, string Description, DateOnly OccurredOn);

public sealed record StudentAccount(Guid StudentId, decimal Balance, PagedResult<AccountEntry> Entries);

// Charge/payment/waiver responses all carry the resulting balance (payment/waiver also return an entry id we ignore).
public sealed record AccountBalance(decimal Balance);

// Mirrors the API's Students.Domain.ChargeCategory. The API has no string-enum converter, so it binds enums
// by NUMBER — order MUST match the server. System.Text.Json serializes this as its numeric value, which is
// exactly what the charges endpoint expects.
public enum ChargeCategory
{
    Tuition = 0,
    Fee = 1,
    LibraryFine = 2,
    Other = 3,
}

// ---- Academics ---------------------------------------------------------------------------------

public sealed record CourseListItem(Guid Id, string Code, string Title, int Credits, string DepartmentName);

public sealed record CoursePrerequisite(Guid CourseId, string Code, string Title);

public sealed record CourseDetail(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    int Credits,
    string DepartmentName,
    IReadOnlyList<CoursePrerequisite> Prerequisites);

public sealed record SectionListItem(
    Guid Id,
    string CourseCode,
    string CourseTitle,
    string Term,
    string SectionCode,
    string InstructorName,
    int Capacity,
    int EnrolledCount,
    string Status);

public sealed record SectionSchedule(string Days, TimeOnly StartTime, TimeOnly EndTime, string Room);

public sealed record SectionDetail(
    Guid Id,
    Guid CourseId,
    string CourseCode,
    string CourseTitle,
    Guid InstructorId,
    string InstructorName,
    string Term,
    string SectionCode,
    int Capacity,
    int EnrolledCount,
    int WaitlistCount,
    string Status,
    SectionSchedule Schedule);

public sealed record RosterEntry(Guid StudentId, string StudentName, string Status, int? WaitlistPosition, DateOnly EnrolledOn);

public sealed record EnrollResult(Guid EnrollmentId, string Status, int? WaitlistPosition);

public sealed record GradeResult(Guid StudentId, string Grade, decimal Points);

public sealed record TranscriptEntry(string CourseCode, string CourseTitle, int Credits, string Term, string Grade, decimal GradePoints);

public sealed record Transcript(
    Guid StudentId,
    IReadOnlyList<TranscriptEntry> Entries,
    decimal CumulativeGpa,
    int EarnedCredits,
    int AttemptedCredits,
    string Standing);
