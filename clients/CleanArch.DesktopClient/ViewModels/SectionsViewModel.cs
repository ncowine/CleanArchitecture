using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Section search, with the student in context so a selected section can enrol them.</summary>
public sealed class SectionsViewModel : ViewModelBase, INavigationAware
{
    private readonly IAcademicsApiClient _academics;
    private readonly INavigationService _navigation;

    public SectionsViewModel(IAcademicsApiClient academics, INavigationService navigation)
    {
        _academics = academics;
        _navigation = navigation;

        SearchCommand = new DelegateCommand(async () => { Page = 1; await SearchAsync(); });
        NextPageCommand = new DelegateCommand(async () => { Page++; await SearchAsync(); }, () => Page < TotalPages)
            .ObservesProperty(() => Page).ObservesProperty(() => TotalPages);
        PreviousPageCommand = new DelegateCommand(async () => { Page--; await SearchAsync(); }, () => Page > 1)
            .ObservesProperty(() => Page);
        ViewDetailCommand = new DelegateCommand(ViewDetail, () => SelectedSection is not null)
            .ObservesProperty(() => SelectedSection);
        BrowseCoursesCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Courses, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    public ObservableCollection<SectionListItem> Sections { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private string? _termFilter;
    public string? TermFilter { get => _termFilter; set => SetProperty(ref _termFilter, value); }

    private int _page = 1;
    public int Page { get => _page; set => SetProperty(ref _page, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

    private int _totalPages;
    public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private SectionListItem? _selectedSection;
    public SectionListItem? SelectedSection { get => _selectedSection; set => SetProperty(ref _selectedSection, value); }

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand NextPageCommand { get; }
    public DelegateCommand PreviousPageCommand { get; }
    public DelegateCommand ViewDetailCommand { get; }
    public DelegateCommand BrowseCoursesCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return SearchAsync();
    }

    public Task SearchAsync() => RunAsync(async () =>
    {
        var term = string.IsNullOrWhiteSpace(TermFilter) ? null : TermFilter;
        var result = await _academics.SearchSectionsAsync(Page, PageSize, term);

        Sections.Clear();
        foreach (var section in result.Items)
        {
            Sections.Add(section);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
    });

    private void ViewDetail()
    {
        if (SelectedSection is null)
        {
            return;
        }

        _navigation.NavigateTo(ViewNames.SectionDetail, new NavigationParameters
        {
            { ViewNames.StudentIdParameter, StudentId },
            { ViewNames.SectionIdParameter, SelectedSection.Id },
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
