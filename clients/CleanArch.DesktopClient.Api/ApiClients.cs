namespace CleanArch.DesktopClient.Api;

public interface IStudentsApiClient
{
    Task<PagedResult<StudentSummary>> SearchAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<StudentDetail?> GetDetailAsync(Guid studentId, CancellationToken ct = default);
    Task<PagedResult<StudentHold>> GetHoldsAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task WithdrawAsync(Guid studentId, CancellationToken ct = default);
}

public interface ILibraryApiClient
{
    Task<StudentLoans> GetLoansAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task BorrowAsync(Guid studentId, Guid copyId, CancellationToken ct = default);
    Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default);
    Task<ReturnLoanResult> ReturnAsync(Guid copyId, CancellationToken ct = default);
    Task<RenewLoanResult> RenewAsync(Guid copyId, CancellationToken ct = default);

    // Catalog
    Task<PagedResult<BookListItem>> SearchBooksAsync(int page, int pageSize, string? category, string? search, CancellationToken ct = default);
    Task<PagedResult<CopyListItem>> GetCopiesAsync(Guid bookId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    // Reservations
    Task<ReserveResult> ReserveAsync(Guid studentId, Guid bookId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentReservation>> GetStudentReservationsAsync(Guid studentId, CancellationToken ct = default);
    Task CancelReservationAsync(Guid reservationId, CancellationToken ct = default);
}

public interface IBillingApiClient
{
    Task<StudentAccount> GetAccountAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<decimal> ChargeAsync(Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken ct = default);
    Task<decimal> RecordPaymentAsync(Guid studentId, decimal amount, string? description, CancellationToken ct = default);
    Task<decimal> WaiveAsync(Guid studentId, decimal amount, string description, CancellationToken ct = default);
}

public interface IAcademicsApiClient
{
    Task<Transcript> GetTranscriptAsync(Guid studentId, CancellationToken ct = default);

    Task<PagedResult<SectionListItem>> SearchSectionsAsync(int page, int pageSize, string? term, CancellationToken ct = default);
    Task<SectionDetail?> GetSectionAsync(Guid sectionId, CancellationToken ct = default);
    Task<PagedResult<RosterEntry>> GetRosterAsync(Guid sectionId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<EnrollResult> EnrollAsync(Guid sectionId, Guid studentId, CancellationToken ct = default);
    Task DropAsync(Guid sectionId, Guid studentId, CancellationToken ct = default);
    Task<GradeResult> RecordGradeAsync(Guid sectionId, Guid studentId, string grade, CancellationToken ct = default);
    Task CancelSectionAsync(Guid sectionId, CancellationToken ct = default);

    Task<PagedResult<CourseListItem>> SearchCoursesAsync(int page, int pageSize, string? department, CancellationToken ct = default);
    Task<CourseDetail?> GetCourseAsync(Guid courseId, CancellationToken ct = default);
}

internal sealed class StudentsApiClient : IStudentsApiClient
{
    private readonly HttpClient _http;
    public StudentsApiClient(HttpClient http) => _http = http;

    public Task<PagedResult<StudentSummary>> SearchAsync(int page, int pageSize, string? status, CancellationToken ct = default) =>
        // List read is a POST with paging/filters in the body (the API's house convention).
        _http.PostJsonAsync<PagedResult<StudentSummary>>("students/search", new { page, pageSize, status }, ct);

    public Task<StudentDetail?> GetDetailAsync(Guid studentId, CancellationToken ct = default) =>
        // A missing student is a normal empty result here, not an error — 404 comes back as null.
        _http.GetJsonOrNotFoundAsync<StudentDetail>($"students/{studentId}/detail", ct);

    public Task<PagedResult<StudentHold>> GetHoldsAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        _http.GetJsonAsync<PagedResult<StudentHold>>($"students/{studentId}/holds?page={page}&pageSize={pageSize}", ct);

    public Task WithdrawAsync(Guid studentId, CancellationToken ct = default) =>
        _http.PostAsync($"students/{studentId}/withdraw", body: null, ct);
}

internal sealed class LibraryApiClient : ILibraryApiClient
{
    private readonly HttpClient _http;
    public LibraryApiClient(HttpClient http) => _http = http;

    public Task<StudentLoans> GetLoansAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        _http.GetJsonAsync<StudentLoans>($"library/students/{studentId}/loans?page={page}&pageSize={pageSize}", ct);

    public Task BorrowAsync(Guid studentId, Guid copyId, CancellationToken ct = default) =>
        _http.PostAsync("library/loans", new { studentId, copyId }, ct);

    public Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default) =>
        _http.PostJsonAsync<AssessFineResult>($"library/loans/{loanId}/fines", new { amount }, ct);

    // Return/renew are keyed by copy id (the physical item), not the loan id.
    public Task<ReturnLoanResult> ReturnAsync(Guid copyId, CancellationToken ct = default) =>
        _http.PostJsonAsync<ReturnLoanResult>($"library/copies/{copyId}/return", body: null, ct);

    public Task<RenewLoanResult> RenewAsync(Guid copyId, CancellationToken ct = default) =>
        _http.PostJsonAsync<RenewLoanResult>($"library/copies/{copyId}/renew", body: null, ct);

    public Task<PagedResult<BookListItem>> SearchBooksAsync(int page, int pageSize, string? category, string? search, CancellationToken ct = default) =>
        // Catalog search follows the house convention: a POST with paging/filters in the body.
        _http.PostJsonAsync<PagedResult<BookListItem>>("library/books/search", new { page, pageSize, category, search }, ct);

    public Task<PagedResult<CopyListItem>> GetCopiesAsync(Guid bookId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        _http.GetJsonAsync<PagedResult<CopyListItem>>($"library/books/{bookId}/copies?page={page}&pageSize={pageSize}", ct);

    public Task<ReserveResult> ReserveAsync(Guid studentId, Guid bookId, CancellationToken ct = default) =>
        _http.PostJsonAsync<ReserveResult>($"library/books/{bookId}/reservations", new { studentId }, ct);

    public Task<IReadOnlyList<StudentReservation>> GetStudentReservationsAsync(Guid studentId, CancellationToken ct = default) =>
        _http.GetJsonAsync<IReadOnlyList<StudentReservation>>($"library/students/{studentId}/reservations", ct);

    public Task CancelReservationAsync(Guid reservationId, CancellationToken ct = default) =>
        _http.PostAsync($"library/reservations/{reservationId}/cancel", body: null, ct);
}

internal sealed class BillingApiClient : IBillingApiClient
{
    private readonly HttpClient _http;
    public BillingApiClient(HttpClient http) => _http = http;

    public Task<StudentAccount> GetAccountAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        _http.GetJsonAsync<StudentAccount>($"students/{studentId}/account?page={page}&pageSize={pageSize}", ct);

    public async Task<decimal> ChargeAsync(Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken ct = default) =>
        // category serializes as its numeric value — the API binds the enum by number (no string-enum converter).
        (await _http.PostJsonAsync<AccountBalance>(
            $"students/{studentId}/account/charges", new { amount, category, description }, ct)).Balance;

    public async Task<decimal> RecordPaymentAsync(Guid studentId, decimal amount, string? description, CancellationToken ct = default) =>
        (await _http.PostJsonAsync<AccountBalance>(
            $"students/{studentId}/account/payments", new { amount, description }, ct)).Balance;

    public async Task<decimal> WaiveAsync(Guid studentId, decimal amount, string description, CancellationToken ct = default) =>
        (await _http.PostJsonAsync<AccountBalance>(
            $"students/{studentId}/account/waivers", new { amount, description }, ct)).Balance;
}

internal sealed class AcademicsApiClient : IAcademicsApiClient
{
    private readonly HttpClient _http;
    public AcademicsApiClient(HttpClient http) => _http = http;

    public Task<Transcript> GetTranscriptAsync(Guid studentId, CancellationToken ct = default) =>
        _http.GetJsonAsync<Transcript>($"students/{studentId}/transcript", ct);

    public Task<PagedResult<SectionListItem>> SearchSectionsAsync(int page, int pageSize, string? term, CancellationToken ct = default) =>
        _http.PostJsonAsync<PagedResult<SectionListItem>>("sections/search", new { page, pageSize, term }, ct);

    public Task<SectionDetail?> GetSectionAsync(Guid sectionId, CancellationToken ct = default) =>
        _http.GetJsonOrNotFoundAsync<SectionDetail>($"sections/{sectionId}", ct);

    public Task<PagedResult<RosterEntry>> GetRosterAsync(Guid sectionId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        _http.GetJsonAsync<PagedResult<RosterEntry>>($"sections/{sectionId}/roster?page={page}&pageSize={pageSize}", ct);

    public Task<EnrollResult> EnrollAsync(Guid sectionId, Guid studentId, CancellationToken ct = default) =>
        _http.PostJsonAsync<EnrollResult>($"sections/{sectionId}/enroll", new { studentId }, ct);

    public Task DropAsync(Guid sectionId, Guid studentId, CancellationToken ct = default) =>
        _http.PostAsync($"sections/{sectionId}/drop", new { studentId }, ct);

    public Task<GradeResult> RecordGradeAsync(Guid sectionId, Guid studentId, string grade, CancellationToken ct = default) =>
        _http.PostJsonAsync<GradeResult>($"sections/{sectionId}/grades", new { studentId, grade }, ct);

    public Task CancelSectionAsync(Guid sectionId, CancellationToken ct = default) =>
        _http.PostAsync($"sections/{sectionId}/cancel", body: null, ct);

    public Task<PagedResult<CourseListItem>> SearchCoursesAsync(int page, int pageSize, string? department, CancellationToken ct = default) =>
        _http.PostJsonAsync<PagedResult<CourseListItem>>("courses/search", new { page, pageSize, department }, ct);

    public Task<CourseDetail?> GetCourseAsync(Guid courseId, CancellationToken ct = default) =>
        _http.GetJsonOrNotFoundAsync<CourseDetail>($"courses/{courseId}", ct);
}
