using System.Net.Http.Json;

namespace CleanArch.DesktopClient.Api;

public interface IStudentsApiClient
{
    Task<PagedResult<StudentSummary>> SearchAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<StudentDetail?> GetDetailAsync(Guid studentId, CancellationToken ct = default);
    Task WithdrawAsync(Guid studentId, CancellationToken ct = default);
}

public interface ILibraryApiClient
{
    Task<StudentLoans> GetLoansAsync(Guid studentId, CancellationToken ct = default);
    Task BorrowAsync(Guid studentId, string bookTitle, DateOnly dueOn, CancellationToken ct = default);
    Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default);
}

internal sealed class StudentsApiClient : IStudentsApiClient
{
    private readonly HttpClient _http;
    public StudentsApiClient(HttpClient http) => _http = http;

    public async Task<PagedResult<StudentSummary>> SearchAsync(int page, int pageSize, string? status, CancellationToken ct = default)
    {
        // List read is a POST with paging/filters in the body (the API's house convention).
        var response = await _http.PostAsJsonAsync("students/search", new { page, pageSize, status }, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<StudentSummary>>(ct))!;
    }

    public Task<StudentDetail?> GetDetailAsync(Guid studentId, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<StudentDetail>($"students/{studentId}/detail", ct);

    public async Task WithdrawAsync(Guid studentId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"students/{studentId}/withdraw", content: null, ct);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed class LibraryApiClient : ILibraryApiClient
{
    private readonly HttpClient _http;
    public LibraryApiClient(HttpClient http) => _http = http;

    public async Task<StudentLoans> GetLoansAsync(Guid studentId, CancellationToken ct = default) =>
        (await _http.GetFromJsonAsync<StudentLoans>($"library/students/{studentId}/loans", ct))!;

    public async Task BorrowAsync(Guid studentId, string bookTitle, DateOnly dueOn, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("library/loans", new { studentId, bookTitle, dueOn }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AssessFineResult> AssessFineAsync(Guid loanId, decimal amount, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"library/loans/{loanId}/fines", new { amount }, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssessFineResult>(ct))!;
    }
}
