using System.Collections.ObjectModel;
using System.Globalization;
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

        BorrowCommand = new DelegateCommand(async () => await BorrowAsync(), () => !string.IsNullOrWhiteSpace(BookTitle))
            .ObservesProperty(() => BookTitle);
        AssessFineCommand = new DelegateCommand(async () => await AssessFineAsync(), () => SelectedLoan is not null && FineAmount > 0)
            .ObservesProperty(() => SelectedLoan).ObservesProperty(() => FineAmount);
        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    public ObservableCollection<LoanSummary> Loans { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private string? _studentName;
    public string? StudentName { get => _studentName; private set => SetProperty(ref _studentName, value); }

    private string _bookTitle = string.Empty;
    public string BookTitle { get => _bookTitle; set => SetProperty(ref _bookTitle, value); }

    private string _dueOnText = DateOnly.FromDateTime(DateTime.Today.AddMonths(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string DueOnText { get => _dueOnText; set => SetProperty(ref _dueOnText, value); }

    private decimal _fineAmount;
    public decimal FineAmount { get => _fineAmount; set => SetProperty(ref _fineAmount, value); }

    private LoanSummary? _selectedLoan;
    public LoanSummary? SelectedLoan { get => _selectedLoan; set => SetProperty(ref _selectedLoan, value); }

    private bool _lastHoldRequested;
    public bool LastHoldRequested { get => _lastHoldRequested; private set => SetProperty(ref _lastHoldRequested, value); }

    public DelegateCommand BorrowCommand { get; }
    public DelegateCommand AssessFineCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return RunAsync(LoadLoansCoreAsync);
    }

    public Task BorrowAsync() => RunAsync(async () =>
    {
        if (!DateOnly.TryParse(DueOnText, CultureInfo.InvariantCulture, out var dueOn))
        {
            throw new FormatException($"'{DueOnText}' is not a valid date (use yyyy-MM-dd).");
        }

        await _library.BorrowAsync(StudentId, BookTitle, dueOn);
        BookTitle = string.Empty;
        await LoadLoansCoreAsync();
    });

    public Task AssessFineAsync() => RunAsync(async () =>
    {
        if (SelectedLoan is null)
        {
            return;
        }

        var result = await _library.AssessFineAsync(SelectedLoan.Id, FineAmount);
        LastHoldRequested = result.HoldRequested;
        await LoadLoansCoreAsync();
    });

    private async Task LoadLoansCoreAsync()
    {
        var result = await _library.GetLoansAsync(StudentId);
        StudentName = result.StudentName;

        Loans.Clear();
        foreach (var loan in result.Loans)
        {
            Loans.Add(loan);
        }
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        if (navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId))
        {
            _ = LoadAsync(studentId);
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
