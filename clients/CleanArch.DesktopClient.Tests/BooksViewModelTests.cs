using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class BooksViewModelTests
{
    private static BookListItem Book(string title = "SICP") =>
        new(Guid.NewGuid(), "978-0", title, "Abelson", "Technology", 3);

    private static PagedResult<BookListItem> Paged(params BookListItem[] books) =>
        new(books, 1, 20, books.Length, books.Length == 0 ? 0 : 1);

    [Fact]
    public async Task Load_searches_for_student_and_populates_books_and_totals()
    {
        var studentId = Guid.NewGuid();
        var api = new FakeLibraryApiClient { Books = new PagedResult<BookListItem>(new[] { Book() }, 1, 20, 7, 1) };
        var vm = new BooksViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(studentId);

        Assert.Equal(studentId, vm.StudentId);
        Assert.Single(vm.Books);
        Assert.Equal(7, vm.TotalCount);
        Assert.Equal(1, vm.TotalPages);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Search_passes_filters_and_blanks_become_null()
    {
        var api = new FakeLibraryApiClient { Books = Paged() };
        var vm = new BooksViewModel(api, new FakeNavigationService())
        {
            CategoryFilter = "Technology",
            SearchText = "   ", // whitespace should be sent as null
        };

        await vm.SearchAsync();

        var (page, pageSize, category, search) = Assert.Single(api.BookSearches);
        Assert.Equal(1, page);
        Assert.Equal(20, pageSize);
        Assert.Equal("Technology", category);
        Assert.Null(search);
    }

    [Fact]
    public async Task Next_page_advances_and_requests_the_next_page()
    {
        var api = new FakeLibraryApiClient { Books = new PagedResult<BookListItem>(new[] { Book() }, 1, 20, 100, 5) };
        var vm = new BooksViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid());

        vm.NextPageCommand.Execute();

        Assert.Equal(2, vm.Page);
        Assert.Equal(2, api.BookSearches[^1].page); // most recent request asked for page 2
    }

    [Fact]
    public async Task View_copies_navigates_with_student_and_book_ids()
    {
        var studentId = Guid.NewGuid();
        var book = Book();
        var navigation = new FakeNavigationService();
        var api = new FakeLibraryApiClient { Books = Paged(book) };
        var vm = new BooksViewModel(api, navigation);
        await vm.LoadAsync(studentId);

        vm.SelectedBook = book;
        vm.ViewCopiesCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.BookCopies, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
        Assert.Equal(book.Id, parameters.GetValue<Guid>(ViewNames.BookIdParameter));
    }

    [Fact]
    public void View_copies_is_disabled_without_a_selection()
    {
        var vm = new BooksViewModel(new FakeLibraryApiClient(), new FakeNavigationService());

        Assert.False(vm.ViewCopiesCommand.CanExecute());
    }

    [Fact]
    public async Task Back_returns_to_loans_for_the_same_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new BooksViewModel(new FakeLibraryApiClient { Books = Paged() }, navigation);
        await vm.LoadAsync(studentId);

        vm.BackCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Loans, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }

    [Fact]
    public async Task Search_error_is_surfaced_and_busy_cleared()
    {
        var api = new FakeLibraryApiClient { BookSearchError = new InvalidOperationException("boom") };
        var vm = new BooksViewModel(api, new FakeNavigationService());

        await vm.SearchAsync();

        Assert.Equal("boom", vm.Error);
        Assert.False(vm.IsBusy);
    }
}
