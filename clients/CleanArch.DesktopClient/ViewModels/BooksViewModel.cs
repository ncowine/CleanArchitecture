using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Catalog search. Carries the student context so a picked copy can be borrowed/reserved for them.</summary>
public sealed class BooksViewModel : ViewModelBase, INavigationAware
{
    private readonly ILibraryApiClient _library;
    private readonly INavigationService _navigation;

    public BooksViewModel(ILibraryApiClient library, INavigationService navigation)
    {
        _library = library;
        _navigation = navigation;

        SearchCommand = new DelegateCommand(async () => { Page = 1; await SearchAsync(); });
        NextPageCommand = new DelegateCommand(async () => { Page++; await SearchAsync(); }, () => Page < TotalPages)
            .ObservesProperty(() => Page).ObservesProperty(() => TotalPages);
        PreviousPageCommand = new DelegateCommand(async () => { Page--; await SearchAsync(); }, () => Page > 1)
            .ObservesProperty(() => Page);
        ViewCopiesCommand = new DelegateCommand(ViewCopies, () => SelectedBook is not null)
            .ObservesProperty(() => SelectedBook);
        BackCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Loans, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
    }

    public ObservableCollection<BookListItem> Books { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private string? _categoryFilter;
    public string? CategoryFilter { get => _categoryFilter; set => SetProperty(ref _categoryFilter, value); }

    private string? _searchText;
    public string? SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    private int _page = 1;
    public int Page { get => _page; set => SetProperty(ref _page, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

    private int _totalPages;
    public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private BookListItem? _selectedBook;
    public BookListItem? SelectedBook { get => _selectedBook; set => SetProperty(ref _selectedBook, value); }

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand NextPageCommand { get; }
    public DelegateCommand PreviousPageCommand { get; }
    public DelegateCommand ViewCopiesCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return SearchAsync();
    }

    public Task SearchAsync() => RunAsync(async () =>
    {
        var category = string.IsNullOrWhiteSpace(CategoryFilter) ? null : CategoryFilter;
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;
        var result = await _library.SearchBooksAsync(Page, PageSize, category, search);

        Books.Clear();
        foreach (var book in result.Items)
        {
            Books.Add(book);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
    });

    private void ViewCopies()
    {
        if (SelectedBook is null)
        {
            return;
        }

        _navigation.NavigateTo(ViewNames.BookCopies, new NavigationParameters
        {
            { ViewNames.StudentIdParameter, StudentId },
            { ViewNames.BookIdParameter, SelectedBook.Id },
        });
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId);
        _ = LoadAsync(studentId);
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
