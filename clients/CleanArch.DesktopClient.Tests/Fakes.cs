using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Regions;

namespace CleanArch.DesktopClient.Tests;

internal sealed class FakeStudentsApiClient : IStudentsApiClient
{
    public PagedResult<StudentSummary> NextResult { get; set; } =
        new(new List<StudentSummary>(), 1, 20, 0, 0);
    public StudentDetail? Detail { get; set; }
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

    public Task WithdrawAsync(Guid studentId, CancellationToken ct = default)
    {
        Withdrawn.Add(studentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLibraryApiClient : ILibraryApiClient
{
    public StudentLoans Loans { get; set; } = new(Guid.Empty, string.Empty, string.Empty, new List<LoanSummary>());
    public List<(Guid studentId, string bookTitle, DateOnly dueOn)> Borrows { get; } = new();
    public List<(Guid loanId, decimal amount)> Fines { get; } = new();
    public AssessFineResult FineResult { get; set; } = new(0m, false);

    public Task<StudentLoans> GetLoansAsync(Guid studentId, CancellationToken ct = default) => Task.FromResult(Loans);

    public Task BorrowAsync(Guid studentId, string bookTitle, DateOnly dueOn, CancellationToken ct = default)
    {
        Borrows.Add((studentId, bookTitle, dueOn));
        return Task.CompletedTask;
    }

    public Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default)
    {
        Fines.Add((loanId, amount));
        return Task.FromResult(FineResult);
    }
}

internal sealed class FakeTokenStore : ITokenStore
{
    public bool IsSignedIn => AccessToken is not null;
    public string? Actor { get; private set; }
    public string? AccessToken { get; private set; }
    public List<string> SignIns { get; } = new();

    public Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        SignIns.Add(actor);
        Actor = actor;
        AccessToken = "fake-token";
        return Task.CompletedTask;
    }

    public void SignOut()
    {
        AccessToken = null;
        Actor = null;
    }
}

internal sealed class FakeNavigationService : INavigationService
{
    public List<(string View, NavigationParameters? Parameters)> Navigations { get; } = new();

    public void NavigateTo(string viewName, NavigationParameters? parameters = null) =>
        Navigations.Add((viewName, parameters));
}
