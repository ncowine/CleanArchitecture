using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class LoansViewModelTests
{
    private static LoanSummary Loan() =>
        new(Guid.NewGuid(), "SICP", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), null, 0m, 0);

    private static PagedResult<LoanSummary> Paged(params LoanSummary[] loans) =>
        new(loans, 1, 20, loans.Length, loans.Length == 0 ? 0 : 1);

    [Fact]
    public async Task Load_populates_loans_and_student_name()
    {
        var id = Guid.NewGuid();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "Grace Hopper", "Active", Paged(Loan())),
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.Equal("Grace Hopper", vm.StudentName);
        Assert.Single(vm.Loans);
    }

    [Fact]
    public async Task Borrow_parses_copy_id_calls_api_and_reloads()
    {
        var id = Guid.NewGuid();
        var copyId = Guid.NewGuid();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged()),
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.CopyIdText = copyId.ToString();
        await vm.BorrowAsync();

        var borrow = Assert.Single(api.Borrows);
        Assert.Equal(copyId, borrow.copyId);
        Assert.Equal(string.Empty, vm.CopyIdText); // cleared after borrow
    }

    [Fact]
    public async Task Borrow_with_a_bad_copy_id_surfaces_error_and_does_not_call_api()
    {
        var id = Guid.NewGuid();
        var api = new FakeLibraryApiClient { Loans = new StudentLoans(id, "X", "Active", Paged()) };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.CopyIdText = "not-a-guid";
        await vm.BorrowAsync();

        Assert.Empty(api.Borrows);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public async Task AssessFine_uses_selected_loan_and_records_hold_flag()
    {
        var id = Guid.NewGuid();
        var loan = Loan();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged(loan)),
            FineResult = new AssessFineResult(25m, true),
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.SelectedLoan = loan;
        vm.FineAmount = 25m;
        await vm.AssessFineAsync();

        var fine = Assert.Single(api.Fines);
        Assert.Equal(loan.Id, fine.loanId);
        Assert.Equal(25m, fine.amount);
        Assert.True(vm.LastHoldRequested);
    }
}
