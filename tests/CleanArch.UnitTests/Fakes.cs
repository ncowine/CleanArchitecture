using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Library.Application.Abstractions;
using Library.Application.Catalog;
using Library.Domain;
using Students.Application.Abstractions;
using Students.Application.Academics;
using Students.Contracts;
using Students.Domain;

namespace CleanArch.UnitTests;

internal sealed class FakeLoanRepository : ILoanRepository
{
    private readonly Dictionary<Guid, Loan> _loans = new();

    /// <summary>Value returned by <see cref="GetFineTotalAsync"/> — set per test.</summary>
    public decimal FineTotal { get; set; }

    public List<Loan> Added { get; } = new();

    public void Seed(Loan loan) => _loans[loan.Id] = loan;

    public Task AddAsync(Loan loan, CancellationToken cancellationToken)
    {
        Added.Add(loan);
        _loans[loan.Id] = loan;
        return Task.CompletedTask;
    }

    public Task<PagedResult<Loan>> GetByStudentAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = _loans.Values
            .Where(loan => loan.StudentId == studentId)
            .OrderByDescending(loan => loan.BorrowedOn)
            .ToList();

        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<Loan>(items, page, pageSize, all.Count));
    }

    public Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken) =>
        Task.FromResult(_loans.TryGetValue(loanId, out var loan) ? loan : null);

    public Task<Loan?> GetActiveByCopyAsync(Guid copyId, CancellationToken cancellationToken) =>
        Task.FromResult(_loans.Values.FirstOrDefault(loan => loan.CopyId == copyId && !loan.IsReturned));

    public Task<int> CountActiveByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult(_loans.Values.Count(loan => loan.StudentId == studentId && !loan.IsReturned));

    public Task<decimal> GetFineTotalAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult(FineTotal);
}

internal sealed class FakeOutbox : IOutbox
{
    public List<object> Events { get; } = new();

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class => Events.Add(integrationEvent);
}

internal sealed class FakeStudentDirectory : IStudentDirectory
{
    private readonly StudentSummary? _summary;

    public FakeStudentDirectory(StudentSummary? summary) => _summary = summary;

    public Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult(_summary);
}

internal sealed class FakeStudentRepository : IStudentRepository
{
    private readonly Student? _student;

    public FakeStudentRepository(Student? student = null) => _student = student;

    public Task AddAsync(Student student, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Student?> GetAsync(Guid studentId, CancellationToken cancellationToken) => Task.FromResult(_student);

    public List<StudentHold> AddedHolds { get; } = new();

    public Task<PagedResult<StudentHold>> GetHoldsAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<StudentHold>(Array.Empty<StudentHold>(), page, pageSize, 0));

    public Task AddHoldAsync(StudentHold hold, CancellationToken cancellationToken)
    {
        AddedHolds.Add(hold);
        return Task.CompletedTask;
    }
}

internal sealed class FakeStudentCacheInvalidator : IStudentCacheInvalidator
{
    public List<Guid> Removed { get; } = new();

    public Task RemoveAsync(Guid studentId, CancellationToken cancellationToken)
    {
        Removed.Add(studentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCourseRepository : ICourseRepository
{
    private readonly Dictionary<Guid, Course> _courses = new();

    public void Seed(Course course) => _courses[course.Id] = course;

    public Task AddAsync(Course course, CancellationToken cancellationToken)
    {
        _courses[course.Id] = course;
        return Task.CompletedTask;
    }

    public Task<Course?> GetAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult(_courses.TryGetValue(courseId, out var course) ? course : null);

    public Task<bool> ExistsAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult(_courses.ContainsKey(courseId));
}

internal sealed class FakeInstructorRepository : IInstructorRepository
{
    private readonly HashSet<Guid> _ids = new();

    public List<Instructor> Added { get; } = new();

    public void Seed(Guid instructorId) => _ids.Add(instructorId);

    public Task AddAsync(Instructor instructor, CancellationToken cancellationToken)
    {
        Added.Add(instructor);
        _ids.Add(instructor.Id);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid instructorId, CancellationToken cancellationToken) =>
        Task.FromResult(_ids.Contains(instructorId));
}

internal sealed class FakeCourseSectionRepository : ICourseSectionRepository
{
    private readonly Dictionary<Guid, CourseSection> _sections = new();

    public void Seed(CourseSection section) => _sections[section.Id] = section;

    public Task AddAsync(CourseSection section, CancellationToken cancellationToken)
    {
        _sections[section.Id] = section;
        return Task.CompletedTask;
    }

    public Task<CourseSection?> GetAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult(_sections.TryGetValue(sectionId, out var section) ? section : null);
}

internal sealed class FakeSectionReadService : ISectionReadService
{
    /// <summary>Course ids the student is treated as having satisfied — set per test.</summary>
    public HashSet<Guid> SatisfiedCourseIds { get; } = new();

    public Task<IReadOnlySet<Guid>> GetSatisfiedCourseIdsAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(SatisfiedCourseIds);

    public Task<GetSection.Response?> GetAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult<GetSection.Response?>(null);

    public Task<PagedResult<SearchSections.SectionListItem>> SearchAsync(
        int page, int pageSize, string? term, Guid? courseId, Guid? instructorId, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<SearchSections.SectionListItem>(
            Array.Empty<SearchSections.SectionListItem>(), page, pageSize, 0));

    public Task<PagedResult<GetSectionRoster.RosterEntry>> GetRosterAsync(
        Guid sectionId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<GetSectionRoster.RosterEntry>(
            Array.Empty<GetSectionRoster.RosterEntry>(), page, pageSize, 0));

    public Task<IReadOnlyList<GetStudentSchedule.ScheduledSection>> GetStudentScheduleAsync(
        Guid studentId, string term, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GetStudentSchedule.ScheduledSection>>(
            Array.Empty<GetStudentSchedule.ScheduledSection>());
}

internal sealed class FakeBookRepository : IBookRepository
{
    private readonly Dictionary<Guid, Book> _books = new();

    public void Seed(Book book) => _books[book.Id] = book;

    public Task AddAsync(Book book, CancellationToken cancellationToken)
    {
        _books[book.Id] = book;
        return Task.CompletedTask;
    }

    public Task<Book?> GetAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_books.TryGetValue(bookId, out var book) ? book : null);

    public Task<bool> ExistsAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_books.ContainsKey(bookId));
}

internal sealed class FakeBookCopyRepository : IBookCopyRepository
{
    private readonly Dictionary<Guid, BookCopy> _copies = new();

    public List<BookCopy> Added { get; } = new();

    public void Seed(BookCopy copy) => _copies[copy.Id] = copy;

    public Task AddAsync(BookCopy copy, CancellationToken cancellationToken)
    {
        Added.Add(copy);
        _copies[copy.Id] = copy;
        return Task.CompletedTask;
    }

    public Task<BookCopy?> GetAsync(Guid copyId, CancellationToken cancellationToken) =>
        Task.FromResult(_copies.TryGetValue(copyId, out var copy) ? copy : null);

    public Task<int> CountAvailableAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_copies.Values.Count(copy => copy.BookId == bookId && copy.Status == CopyStatus.Available));
}

internal sealed class FakeReservationRepository : IReservationRepository
{
    private readonly Dictionary<Guid, Reservation> _reservations = new();

    public void Seed(Reservation reservation) => _reservations[reservation.Id] = reservation;

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        _reservations[reservation.Id] = reservation;
        return Task.CompletedTask;
    }

    public Task<Reservation?> GetAsync(Guid reservationId, CancellationToken cancellationToken) =>
        Task.FromResult(_reservations.TryGetValue(reservationId, out var reservation) ? reservation : null);

    public Task<Reservation?> GetNextPendingAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_reservations.Values
            .Where(r => r.BookId == bookId && r.Status == ReservationStatus.Pending)
            .OrderBy(r => r.QueuePosition)
            .FirstOrDefault());

    public Task<IReadOnlyList<Reservation>> GetPendingOrderedAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Reservation>>(_reservations.Values
            .Where(r => r.BookId == bookId && r.Status == ReservationStatus.Pending)
            .OrderBy(r => r.QueuePosition)
            .ToList());

    public Task<int> CountPendingAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_reservations.Values.Count(r => r.BookId == bookId && r.Status == ReservationStatus.Pending));

    public Task<bool> HasActiveForStudentAsync(Guid studentId, Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult(_reservations.Values.Any(r => r.StudentId == studentId && r.BookId == bookId && r.IsActive));
}

internal sealed class FakeBookReadService : IBookReadService
{
    /// <summary>Copy id → book title, set per test for loan-title resolution.</summary>
    public Dictionary<Guid, string> Titles { get; } = new();

    public Task<IReadOnlyDictionary<Guid, string>> GetTitlesByCopyAsync(
        IReadOnlyCollection<Guid> copyIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            copyIds.Where(Titles.ContainsKey).ToDictionary(id => id, id => Titles[id]));

    public Task<GetBook.Response?> GetAsync(Guid bookId, CancellationToken cancellationToken) =>
        Task.FromResult<GetBook.Response?>(null);

    public Task<PagedResult<SearchBooks.BookListItem>> SearchAsync(
        int page, int pageSize, string? category, string? search, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<SearchBooks.BookListItem>(
            Array.Empty<SearchBooks.BookListItem>(), page, pageSize, 0));

    public Task<PagedResult<GetBookCopies.CopyListItem>> GetCopiesAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<GetBookCopies.CopyListItem>(
            Array.Empty<GetBookCopies.CopyListItem>(), page, pageSize, 0));
}

internal sealed class FakeStudentAccountRepository : IStudentAccountRepository
{
    private readonly Dictionary<Guid, StudentAccount> _byStudent = new();

    public List<StudentAccount> Added { get; } = new();

    public void Seed(StudentAccount account) => _byStudent[account.StudentId] = account;

    public Task AddAsync(StudentAccount account, CancellationToken cancellationToken)
    {
        Added.Add(account);
        _byStudent[account.StudentId] = account;
        return Task.CompletedTask;
    }

    public Task<StudentAccount?> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult(_byStudent.TryGetValue(studentId, out var account) ? account : null);
}

internal sealed class FakeStudentOutbox : IStudentOutbox
{
    public List<object> Events { get; } = new();

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class => Events.Add(integrationEvent);
}
