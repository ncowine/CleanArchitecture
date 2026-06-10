using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class LoansViewModelTests
{
    private static LoanSummary Loan() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "SICP", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), null, 0m, 0);

    private static LoanSummary ReturnedLoan() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "SICP", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1),
            new DateOnly(2026, 2, 1), 0m, 0);

    private static StudentReservation Reservation() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "TAOCP", "Pending", 1, null);

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

    [Fact]
    public async Task Load_also_populates_reservations()
    {
        var id = Guid.NewGuid();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged()),
            StudentReservations = new List<StudentReservation> { Reservation() },
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.Single(vm.Reservations);
    }

    [Fact]
    public async Task Return_uses_the_selected_loans_copy_id_and_reloads()
    {
        var id = Guid.NewGuid();
        var loan = Loan();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged(loan)),
            ReturnResult = new ReturnLoanResult(loan.Id, WasOverdue: true, OverdueFine: 2m, TotalFine: 2m, HeldForReservation: false),
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.SelectedLoan = loan;
        await vm.ReturnAsync();

        Assert.Equal(loan.CopyId, Assert.Single(api.Returns));
        Assert.Contains("overdue", vm.Notice);
        Assert.Equal(2, api.GetLoansCallCount); // initial load + reload after return
    }

    [Fact]
    public async Task Renew_uses_the_selected_loans_copy_id_and_reports_due_date()
    {
        var id = Guid.NewGuid();
        var loan = Loan();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged(loan)),
            RenewResult = new RenewLoanResult(loan.Id, new DateOnly(2026, 4, 1), 1),
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.SelectedLoan = loan;
        await vm.RenewAsync();

        Assert.Equal(loan.CopyId, Assert.Single(api.Renewals));
        Assert.Contains("Renewed", vm.Notice);
    }

    [Fact]
    public async Task Return_and_renew_are_disabled_for_an_already_returned_loan()
    {
        var id = Guid.NewGuid();
        var returned = ReturnedLoan();
        var api = new FakeLibraryApiClient { Loans = new StudentLoans(id, "X", "Active", Paged(returned)) };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.SelectedLoan = returned;

        Assert.False(vm.ReturnCommand.CanExecute());
        Assert.False(vm.RenewCommand.CanExecute());
    }

    [Fact]
    public async Task Cancel_reservation_calls_api_and_reloads()
    {
        var id = Guid.NewGuid();
        var reservation = Reservation();
        var api = new FakeLibraryApiClient
        {
            Loans = new StudentLoans(id, "X", "Active", Paged()),
            StudentReservations = new List<StudentReservation> { reservation },
        };
        var vm = new LoansViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(id);

        vm.SelectedReservation = reservation;
        await vm.CancelReservationAsync();

        Assert.Equal(reservation.ReservationId, Assert.Single(api.CancelledReservations));
        Assert.Equal(2, api.GetLoansCallCount); // initial load + reload after cancel
    }

    [Fact]
    public async Task Browse_catalog_navigates_to_books_with_the_student()
    {
        var id = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var api = new FakeLibraryApiClient { Loans = new StudentLoans(id, "X", "Active", Paged()) };
        var vm = new LoansViewModel(api, navigation);
        await vm.LoadAsync(id);

        vm.BrowseCatalogCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Books, view);
        Assert.Equal(id, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }
}
