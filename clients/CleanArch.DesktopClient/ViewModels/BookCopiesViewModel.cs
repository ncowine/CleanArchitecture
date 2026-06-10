using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>
/// A book's physical copies, with the student in context. This is where borrowing happens by
/// picking an available copy (instead of pasting a copy id), plus per-copy return/renew and a
/// book-level reservation when nothing is available.
/// </summary>
public sealed class BookCopiesViewModel : ViewModelBase, INavigationAware
{
    private readonly ILibraryApiClient _library;
    private readonly INavigationService _navigation;

    public BookCopiesViewModel(ILibraryApiClient library, INavigationService navigation)
    {
        _library = library;
        _navigation = navigation;

        BorrowCommand = new DelegateCommand(async () => await BorrowAsync(), () => SelectedCopy is { IsAvailable: true })
            .ObservesProperty(() => SelectedCopy);
        ReturnCommand = new DelegateCommand(async () => await ReturnAsync(), () => SelectedCopy is { IsOnLoan: true })
            .ObservesProperty(() => SelectedCopy);
        RenewCommand = new DelegateCommand(async () => await RenewAsync(), () => SelectedCopy is { IsOnLoan: true })
            .ObservesProperty(() => SelectedCopy);
        ReserveCommand = new DelegateCommand(async () => await ReserveAsync(), () => !HasAvailableCopy)
            .ObservesProperty(() => HasAvailableCopy);
        BackCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Books, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
    }

    public ObservableCollection<CopyListItem> Copies { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private Guid _bookId;
    public Guid BookId { get => _bookId; private set => SetProperty(ref _bookId, value); }

    private CopyListItem? _selectedCopy;
    public CopyListItem? SelectedCopy { get => _selectedCopy; set => SetProperty(ref _selectedCopy, value); }

    private bool _hasAvailableCopy;
    public bool HasAvailableCopy { get => _hasAvailableCopy; private set => SetProperty(ref _hasAvailableCopy, value); }

    private string? _notice;
    public string? Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public DelegateCommand BorrowCommand { get; }
    public DelegateCommand ReturnCommand { get; }
    public DelegateCommand RenewCommand { get; }
    public DelegateCommand ReserveCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId, Guid bookId)
    {
        StudentId = studentId;
        BookId = bookId;
        return RunAsync(LoadCoreAsync);
    }

    public Task BorrowAsync() => RunAsync(async () =>
    {
        if (SelectedCopy is not { IsAvailable: true } copy)
        {
            return;
        }

        await _library.BorrowAsync(StudentId, copy.Id);
        Notice = "Borrowed.";
        await LoadCoreAsync();
    });

    public Task ReturnAsync() => RunAsync(async () =>
    {
        if (SelectedCopy is not { IsOnLoan: true } copy)
        {
            return;
        }

        var result = await _library.ReturnAsync(copy.Id);
        Notice = result.WasOverdue
            ? $"Returned — overdue fine {result.OverdueFine:C}."
            : result.HeldForReservation ? "Returned — held for the next reservation." : "Returned.";
        await LoadCoreAsync();
    });

    public Task RenewAsync() => RunAsync(async () =>
    {
        if (SelectedCopy is not { IsOnLoan: true } copy)
        {
            return;
        }

        var result = await _library.RenewAsync(copy.Id);
        Notice = $"Renewed — now due {result.DueOn} (renewal {result.RenewalCount}).";
        await LoadCoreAsync();
    });

    public Task ReserveAsync() => RunAsync(async () =>
    {
        var result = await _library.ReserveAsync(StudentId, BookId);
        Notice = $"Reserved — queue position {result.QueuePosition}.";
    });

    private async Task LoadCoreAsync()
    {
        var result = await _library.GetCopiesAsync(BookId);

        Copies.Clear();
        foreach (var copy in result.Items)
        {
            Copies.Add(copy);
        }

        HasAvailableCopy = Copies.Any(copy => copy.IsAvailable);
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        var hasStudent = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId);
        var hasBook = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.BookIdParameter, out var bookId);
        if (hasStudent && hasBook)
        {
            _ = LoadAsync(studentId, bookId);
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
