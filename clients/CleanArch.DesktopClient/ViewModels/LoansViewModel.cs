using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

public sealed class LoansViewModel : ViewModelBase, INavigationAware
{
    private readonly ILibraryApiClient _library;
    private readonly INavigationService _navigation;

    public LoansViewModel(ILibraryApiClient library, INavigationService navigation)
    {
        _library = library;
        _navigation = navigation;

        BorrowCommand = new DelegateCommand(async () => await BorrowAsync(), () => Guid.TryParse(CopyIdText, out _))
            .ObservesProperty(() => CopyIdText);
        AssessFineCommand = new DelegateCommand(async () => await AssessFineAsync(), () => SelectedLoan is not null && FineAmount > 0)
            .ObservesProperty(() => SelectedLoan).ObservesProperty(() => FineAmount);
        ReturnCommand = new DelegateCommand(async () => await ReturnAsync(), () => SelectedLoan is { IsActive: true })
            .ObservesProperty(() => SelectedLoan);
        RenewCommand = new DelegateCommand(async () => await RenewAsync(), () => SelectedLoan is { IsActive: true })
            .ObservesProperty(() => SelectedLoan);
        CancelReservationCommand = new DelegateCommand(async () => await CancelReservationAsync(), () => SelectedReservation is not null)
            .ObservesProperty(() => SelectedReservation);
        BrowseCatalogCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Books, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    public ObservableCollection<LoanSummary> Loans { get; } = new();
    public ObservableCollection<StudentReservation> Reservations { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private string? _studentName;
    public string? StudentName { get => _studentName; private set => SetProperty(ref _studentName, value); }

    private string _copyIdText = string.Empty;
    public string CopyIdText { get => _copyIdText; set => SetProperty(ref _copyIdText, value); }

    private decimal _fineAmount;
    public decimal FineAmount { get => _fineAmount; set => SetProperty(ref _fineAmount, value); }

    private LoanSummary? _selectedLoan;
    public LoanSummary? SelectedLoan { get => _selectedLoan; set => SetProperty(ref _selectedLoan, value); }

    private StudentReservation? _selectedReservation;
    public StudentReservation? SelectedReservation { get => _selectedReservation; set => SetProperty(ref _selectedReservation, value); }

    private bool _lastHoldRequested;
    public bool LastHoldRequested { get => _lastHoldRequested; private set => SetProperty(ref _lastHoldRequested, value); }

    private string? _notice;
    public string? Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public DelegateCommand BorrowCommand { get; }
    public DelegateCommand AssessFineCommand { get; }
    public DelegateCommand ReturnCommand { get; }
    public DelegateCommand RenewCommand { get; }
    public DelegateCommand CancelReservationCommand { get; }
    public DelegateCommand BrowseCatalogCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return RunAsync(LoadCoreAsync);
    }

    public Task BorrowAsync() => RunAsync(async () =>
    {
        if (!Guid.TryParse(CopyIdText, out var copyId))
        {
            throw new FormatException($"'{CopyIdText}' is not a valid copy id.");
        }

        await _library.BorrowAsync(StudentId, copyId);
        CopyIdText = string.Empty;
        await LoadCoreAsync();
    });

    public Task AssessFineAsync() => RunAsync(async () =>
    {
        if (SelectedLoan is null)
        {
            return;
        }

        var result = await _library.AssessFineAsync(SelectedLoan.Id, FineAmount);
        LastHoldRequested = result.HoldRequested;
        await LoadCoreAsync();
    });

    public Task ReturnAsync() => RunAsync(async () =>
    {
        if (SelectedLoan is not { IsActive: true } loan)
        {
            return;
        }

        var result = await _library.ReturnAsync(loan.CopyId);
        Notice = result.WasOverdue
            ? $"Returned — overdue fine {result.OverdueFine:C}."
            : result.HeldForReservation ? "Returned — held for the next reservation." : "Returned.";
        await LoadCoreAsync();
    });

    public Task RenewAsync() => RunAsync(async () =>
    {
        if (SelectedLoan is not { IsActive: true } loan)
        {
            return;
        }

        var result = await _library.RenewAsync(loan.CopyId);
        Notice = $"Renewed — now due {result.DueOn} (renewal {result.RenewalCount}).";
        await LoadCoreAsync();
    });

    public Task CancelReservationAsync() => RunAsync(async () =>
    {
        if (SelectedReservation is null)
        {
            return;
        }

        await _library.CancelReservationAsync(SelectedReservation.ReservationId);
        await LoadCoreAsync();
    });

    private async Task LoadCoreAsync()
    {
        var result = await _library.GetLoansAsync(StudentId);
        StudentName = result.StudentName;

        Loans.Clear();
        foreach (var loan in result.Loans.Items)
        {
            Loans.Add(loan);
        }

        var reservations = await _library.GetStudentReservationsAsync(StudentId);
        Reservations.Clear();
        foreach (var reservation in reservations)
        {
            Reservations.Add(reservation);
        }
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        if (navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId))
        {
            _ = LoadAsync(studentId);
        }
    }

    // Always reload on navigation (e.g. returning from the catalog after a borrow) so the lists stay fresh.
    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
