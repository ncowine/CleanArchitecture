using BuildingBlocks.Outbox;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Application.Abstractions;
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

    public Task<IReadOnlyList<Loan>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Loan>>(_loans.Values.Where(loan => loan.StudentId == studentId).ToList());

    public Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken) =>
        Task.FromResult(_loans.TryGetValue(loanId, out var loan) ? loan : null);

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

    public Task<IReadOnlyList<StudentHold>> GetHoldsAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StudentHold>>(Array.Empty<StudentHold>());
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
