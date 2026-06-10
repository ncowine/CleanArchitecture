using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class BookCopiesViewModelTests
{
    private static CopyListItem Copy(string status) =>
        new(Guid.NewGuid(), "BC-001", "Good", status, new DateOnly(2026, 1, 1));

    private static PagedResult<CopyListItem> Paged(params CopyListItem[] copies) =>
        new(copies, 1, 20, copies.Length, copies.Length == 0 ? 0 : 1);

    [Fact]
    public async Task Load_populates_copies_and_flags_availability()
    {
        var api = new FakeLibraryApiClient { CopiesResult = Paged(Copy("Available"), Copy("OnLoan")) };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(2, vm.Copies.Count);
        Assert.True(vm.HasAvailableCopy);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Borrow_available_copy_calls_api_for_student_and_reloads()
    {
        var studentId = Guid.NewGuid();
        var available = Copy("Available");
        var api = new FakeLibraryApiClient { CopiesResult = Paged(available) };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId, Guid.NewGuid());

        vm.SelectedCopy = available;
        await vm.BorrowAsync();

        var borrow = Assert.Single(api.Borrows);
        Assert.Equal(studentId, borrow.studentId);
        Assert.Equal(available.Id, borrow.copyId);
        Assert.Equal(2, api.CopiesRequestedFor.Count); // initial load + reload after borrow
    }

    [Fact]
    public async Task Borrow_is_disabled_unless_selected_copy_is_available()
    {
        var onLoan = Copy("OnLoan");
        var api = new FakeLibraryApiClient { CopiesResult = Paged(onLoan) };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        vm.SelectedCopy = onLoan;

        Assert.False(vm.BorrowCommand.CanExecute());
        Assert.True(vm.ReturnCommand.CanExecute());
        Assert.True(vm.RenewCommand.CanExecute());
    }

    [Fact]
    public async Task Return_on_loan_copy_calls_api_and_reports_overdue_fine()
    {
        var onLoan = Copy("OnLoan");
        var api = new FakeLibraryApiClient
        {
            CopiesResult = Paged(onLoan),
            ReturnResult = new ReturnLoanResult(Guid.NewGuid(), WasOverdue: true, OverdueFine: 3.50m, TotalFine: 3.50m, HeldForReservation: false),
        };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        vm.SelectedCopy = onLoan;
        await vm.ReturnAsync();

        Assert.Equal(onLoan.Id, Assert.Single(api.Returns));
        Assert.Contains("overdue", vm.Notice);
        Assert.Equal(2, api.CopiesRequestedFor.Count); // reloaded after return
    }

    [Fact]
    public async Task Renew_on_loan_copy_calls_api_and_reports_new_due_date()
    {
        var onLoan = Copy("OnLoan");
        var api = new FakeLibraryApiClient
        {
            CopiesResult = Paged(onLoan),
            RenewResult = new RenewLoanResult(Guid.NewGuid(), new DateOnly(2026, 4, 1), 1),
        };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        vm.SelectedCopy = onLoan;
        await vm.RenewAsync();

        Assert.Equal(onLoan.Id, Assert.Single(api.Renewals));
        Assert.Contains("Renewed", vm.Notice);
    }

    [Fact]
    public async Task Reserve_is_offered_only_when_no_copy_is_available_and_calls_api()
    {
        var studentId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var api = new FakeLibraryApiClient
        {
            CopiesResult = Paged(Copy("OnLoan")), // nothing available → reservation makes sense
            ReserveResult = new ReserveResult(Guid.NewGuid(), QueuePosition: 2),
        };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId, bookId);

        Assert.True(vm.ReserveCommand.CanExecute());

        await vm.ReserveAsync();

        var reservation = Assert.Single(api.Reservations);
        Assert.Equal(studentId, reservation.studentId);
        Assert.Equal(bookId, reservation.bookId);
        Assert.Contains("queue position 2", vm.Notice);
    }

    [Fact]
    public async Task Reserve_is_disabled_when_a_copy_is_available()
    {
        var api = new FakeLibraryApiClient { CopiesResult = Paged(Copy("Available")) };
        var vm = new BookCopiesViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(vm.ReserveCommand.CanExecute());
    }

    [Fact]
    public async Task Back_returns_to_catalog_for_the_same_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new BookCopiesViewModel(new FakeLibraryApiClient(), navigation);
        await vm.LoadAsync(studentId, Guid.NewGuid());

        vm.BackCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Books, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }
}
