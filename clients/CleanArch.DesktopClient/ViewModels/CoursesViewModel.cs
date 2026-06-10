using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Course catalog search; carries the student context so back-navigation returns to their sections.</summary>
public sealed class CoursesViewModel : ViewModelBase, INavigationAware
{
    private readonly IAcademicsApiClient _academics;
    private readonly INavigationService _navigation;

    public CoursesViewModel(IAcademicsApiClient academics, INavigationService navigation)
    {
        _academics = academics;
        _navigation = navigation;

        SearchCommand = new DelegateCommand(async () => { Page = 1; await SearchAsync(); });
        NextPageCommand = new DelegateCommand(async () => { Page++; await SearchAsync(); }, () => Page < TotalPages)
            .ObservesProperty(() => Page).ObservesProperty(() => TotalPages);
        PreviousPageCommand = new DelegateCommand(async () => { Page--; await SearchAsync(); }, () => Page > 1)
            .ObservesProperty(() => Page);
        ViewDetailCommand = new DelegateCommand(ViewDetail, () => SelectedCourse is not null)
            .ObservesProperty(() => SelectedCourse);
        BackCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Sections, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
    }

    public ObservableCollection<CourseListItem> Courses { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private string? _departmentFilter;
    public string? DepartmentFilter { get => _departmentFilter; set => SetProperty(ref _departmentFilter, value); }

    private int _page = 1;
    public int Page { get => _page; set => SetProperty(ref _page, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

    private int _totalPages;
    public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private CourseListItem? _selectedCourse;
    public CourseListItem? SelectedCourse { get => _selectedCourse; set => SetProperty(ref _selectedCourse, value); }

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand NextPageCommand { get; }
    public DelegateCommand PreviousPageCommand { get; }
    public DelegateCommand ViewDetailCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return SearchAsync();
    }

    public Task SearchAsync() => RunAsync(async () =>
    {
        var department = string.IsNullOrWhiteSpace(DepartmentFilter) ? null : DepartmentFilter;
        var result = await _academics.SearchCoursesAsync(Page, PageSize, department);

        Courses.Clear();
        foreach (var course in result.Items)
        {
            Courses.Add(course);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
    });

    private void ViewDetail()
    {
        if (SelectedCourse is null)
        {
            return;
        }

        _navigation.NavigateTo(ViewNames.CourseDetail, new NavigationParameters
        {
            { ViewNames.StudentIdParameter, StudentId },
            { ViewNames.CourseIdParameter, SelectedCourse.Id },
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
