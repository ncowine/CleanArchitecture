using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Regions;

namespace CleanArch.DesktopClient.Tests;

internal sealed class FakeStudentsApiClient : IStudentsApiClient
{
    public PagedResult<StudentSummary> NextResult { get; set; } =
        new(new List<StudentSummary>(), 1, 20, 0, 0);
    public StudentDetail? Detail { get; set; }
    public PagedResult<StudentHold> HoldsResult { get; set; } = new(new List<StudentHold>(), 1, 20, 0, 0);
    public List<Guid> Withdrawn { get; } = new();
    public int SearchCallCount { get; private set; }
    public Exception? SearchError { get; set; }

    public Task<PagedResult<StudentSummary>> SearchAsync(int page, int pageSize, string? status, CancellationToken ct = default)
    {
        SearchCallCount++;
        return SearchError is not null
            ? Task.FromException<PagedResult<StudentSummary>>(SearchError)
            : Task.FromResult(NextResult);
    }

    public Task<StudentDetail?> GetDetailAsync(Guid studentId, CancellationToken ct = default) => Task.FromResult(Detail);

    public Task<PagedResult<StudentHold>> GetHoldsAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        Task.FromResult(HoldsResult);

    public Task WithdrawAsync(Guid studentId, CancellationToken ct = default)
    {
        Withdrawn.Add(studentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLibraryApiClient : ILibraryApiClient
{
    public StudentLoans Loans { get; set; } =
        new(Guid.Empty, string.Empty, string.Empty, new PagedResult<LoanSummary>(new List<LoanSummary>(), 1, 20, 0, 0));
    public PagedResult<BookListItem> Books { get; set; } = new(new List<BookListItem>(), 1, 20, 0, 0);
    public PagedResult<CopyListItem> CopiesResult { get; set; } = new(new List<CopyListItem>(), 1, 20, 0, 0);
    public List<StudentReservation> StudentReservations { get; set; } = new();
    public List<(Guid studentId, Guid copyId)> Borrows { get; } = new();
    public List<(Guid loanId, decimal amount)> Fines { get; } = new();
    public List<Guid> Returns { get; } = new();
    public List<Guid> Renewals { get; } = new();
    public List<(Guid studentId, Guid bookId)> Reservations { get; } = new();
    public List<Guid> CancelledReservations { get; } = new();
    public List<(int page, int pageSize, string? category, string? search)> BookSearches { get; } = new();
    public List<Guid> CopiesRequestedFor { get; } = new();
    public Exception? BookSearchError { get; set; }
    public AssessFineResult FineResult { get; set; } = new(0m, false);
    public ReturnLoanResult ReturnResult { get; set; } = new(Guid.Empty, false, 0m, 0m, false);
    public RenewLoanResult RenewResult { get; set; } = new(Guid.Empty, new DateOnly(2026, 1, 1), 1);
    public ReserveResult ReserveResult { get; set; } = new(Guid.Empty, 1);

    public int GetLoansCallCount { get; private set; }

    public Task<StudentLoans> GetLoansAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        GetLoansCallCount++;
        return Task.FromResult(Loans);
    }

    public Task BorrowAsync(Guid studentId, Guid copyId, CancellationToken ct = default)
    {
        Borrows.Add((studentId, copyId));
        return Task.CompletedTask;
    }

    public Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default)
    {
        Fines.Add((loanId, amount));
        return Task.FromResult(FineResult);
    }

    public Task<ReturnLoanResult> ReturnAsync(Guid copyId, CancellationToken ct = default)
    {
        Returns.Add(copyId);
        return Task.FromResult(ReturnResult);
    }

    public Task<RenewLoanResult> RenewAsync(Guid copyId, CancellationToken ct = default)
    {
        Renewals.Add(copyId);
        return Task.FromResult(RenewResult);
    }

    public Task<PagedResult<BookListItem>> SearchBooksAsync(int page, int pageSize, string? category, string? search, CancellationToken ct = default)
    {
        BookSearches.Add((page, pageSize, category, search));
        return BookSearchError is not null
            ? Task.FromException<PagedResult<BookListItem>>(BookSearchError)
            : Task.FromResult(Books);
    }

    public Task<PagedResult<CopyListItem>> GetCopiesAsync(Guid bookId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        CopiesRequestedFor.Add(bookId);
        return Task.FromResult(CopiesResult);
    }

    public Task<ReserveResult> ReserveAsync(Guid studentId, Guid bookId, CancellationToken ct = default)
    {
        Reservations.Add((studentId, bookId));
        return Task.FromResult(ReserveResult);
    }

    public Task<IReadOnlyList<StudentReservation>> GetStudentReservationsAsync(Guid studentId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StudentReservation>>(StudentReservations);

    public Task CancelReservationAsync(Guid reservationId, CancellationToken ct = default)
    {
        CancelledReservations.Add(reservationId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeBillingApiClient : IBillingApiClient
{
    public StudentAccount Account { get; set; } =
        new(Guid.Empty, 0m, new PagedResult<AccountEntry>(new List<AccountEntry>(), 1, 20, 0, 0));
    public List<(Guid studentId, decimal amount, ChargeCategory category, string description)> Charges { get; } = new();
    public List<(Guid studentId, decimal amount, string? description)> Payments { get; } = new();
    public List<(Guid studentId, decimal amount, string description)> Waivers { get; } = new();
    public decimal NextBalance { get; set; }
    public int GetAccountCallCount { get; private set; }

    public Task<StudentAccount> GetAccountAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        GetAccountCallCount++;
        return Task.FromResult(Account);
    }

    public Task<decimal> ChargeAsync(Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken ct = default)
    {
        Charges.Add((studentId, amount, category, description));
        return Task.FromResult(NextBalance);
    }

    public Task<decimal> RecordPaymentAsync(Guid studentId, decimal amount, string? description, CancellationToken ct = default)
    {
        Payments.Add((studentId, amount, description));
        return Task.FromResult(NextBalance);
    }

    public Task<decimal> WaiveAsync(Guid studentId, decimal amount, string description, CancellationToken ct = default)
    {
        Waivers.Add((studentId, amount, description));
        return Task.FromResult(NextBalance);
    }
}

internal sealed class FakeAcademicsApiClient : IAcademicsApiClient
{
    public Transcript Transcript { get; set; } =
        new(Guid.Empty, new List<TranscriptEntry>(), 0m, 0, 0, "Good");
    public PagedResult<SectionListItem> SectionsResult { get; set; } = new(new List<SectionListItem>(), 1, 20, 0, 0);
    public SectionDetail? Section { get; set; }
    public PagedResult<RosterEntry> RosterResult { get; set; } = new(new List<RosterEntry>(), 1, 20, 0, 0);
    public PagedResult<CourseListItem> CoursesResult { get; set; } = new(new List<CourseListItem>(), 1, 20, 0, 0);
    public CourseDetail? Course { get; set; }
    public EnrollResult EnrollResult { get; set; } = new(Guid.Empty, "Enrolled", null);
    public GradeResult GradeResult { get; set; } = new(Guid.Empty, "A", 4.0m);

    public List<(int page, int pageSize, string? term)> SectionSearches { get; } = new();
    public List<(int page, int pageSize, string? department)> CourseSearches { get; } = new();
    public List<Guid> SectionsRequested { get; } = new();
    public List<Guid> RostersRequested { get; } = new();
    public List<(Guid sectionId, Guid studentId)> Enrollments { get; } = new();
    public List<(Guid sectionId, Guid studentId)> Drops { get; } = new();
    public List<(Guid sectionId, Guid studentId, string grade)> Grades { get; } = new();
    public List<Guid> CancelledSections { get; } = new();

    public Task<Transcript> GetTranscriptAsync(Guid studentId, CancellationToken ct = default) =>
        Task.FromResult(Transcript);

    public Task<PagedResult<SectionListItem>> SearchSectionsAsync(int page, int pageSize, string? term, CancellationToken ct = default)
    {
        SectionSearches.Add((page, pageSize, term));
        return Task.FromResult(SectionsResult);
    }

    public Task<SectionDetail?> GetSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        SectionsRequested.Add(sectionId);
        return Task.FromResult(Section);
    }

    public Task<PagedResult<RosterEntry>> GetRosterAsync(Guid sectionId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        RostersRequested.Add(sectionId);
        return Task.FromResult(RosterResult);
    }

    public Task<EnrollResult> EnrollAsync(Guid sectionId, Guid studentId, CancellationToken ct = default)
    {
        Enrollments.Add((sectionId, studentId));
        return Task.FromResult(EnrollResult);
    }

    public Task DropAsync(Guid sectionId, Guid studentId, CancellationToken ct = default)
    {
        Drops.Add((sectionId, studentId));
        return Task.CompletedTask;
    }

    public Task<GradeResult> RecordGradeAsync(Guid sectionId, Guid studentId, string grade, CancellationToken ct = default)
    {
        Grades.Add((sectionId, studentId, grade));
        return Task.FromResult(GradeResult);
    }

    public Task CancelSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        CancelledSections.Add(sectionId);
        return Task.CompletedTask;
    }

    public Task<PagedResult<CourseListItem>> SearchCoursesAsync(int page, int pageSize, string? department, CancellationToken ct = default)
    {
        CourseSearches.Add((page, pageSize, department));
        return Task.FromResult(CoursesResult);
    }

    public Task<CourseDetail?> GetCourseAsync(Guid courseId, CancellationToken ct = default) =>
        Task.FromResult(Course);
}

internal sealed class FakeAuthSession : IAuthSession
{
    public bool IsSignedIn { get; private set; }
    public string? Actor { get; private set; }
    public List<string> SignIns { get; } = new();

    public Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        SignIns.Add(actor);
        Actor = actor;
        IsSignedIn = true;
        return Task.CompletedTask;
    }

    public void SignOut()
    {
        IsSignedIn = false;
        Actor = null;
    }
}

internal sealed class FakeNavigationService : INavigationService
{
    public List<(string View, NavigationParameters? Parameters)> Navigations { get; } = new();

    public void NavigateTo(string viewName, NavigationParameters? parameters = null) =>
        Navigations.Add((viewName, parameters));
}
