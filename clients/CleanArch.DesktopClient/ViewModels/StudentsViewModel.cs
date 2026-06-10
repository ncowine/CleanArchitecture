using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

public sealed class StudentsViewModel : ViewModelBase, INavigationAware
{
    private readonly IStudentsApiClient _students;
    private readonly INavigationService _navigation;

    public StudentsViewModel(IStudentsApiClient students, INavigationService navigation)
    {
        _students = students;
        _navigation = navigation;

        SearchCommand = new DelegateCommand(async () => await SearchAsync());
        NextPageCommand = new DelegateCommand(async () => { Page++; await SearchAsync(); }, () => Page < TotalPages)
            .ObservesProperty(() => Page).ObservesProperty(() => TotalPages);
        PreviousPageCommand = new DelegateCommand(async () => { Page--; await SearchAsync(); }, () => Page > 1)
            .ObservesProperty(() => Page);
        WithdrawCommand = new DelegateCommand(async () => await WithdrawSelectedAsync(), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
        ViewDetailCommand = new DelegateCommand(() => NavigateForSelected(ViewNames.StudentDetail), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
        ViewLoansCommand = new DelegateCommand(() => NavigateForSelected(ViewNames.Loans), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
        ViewAccountCommand = new DelegateCommand(() => NavigateForSelected(ViewNames.Billing), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
        ViewTranscriptCommand = new DelegateCommand(() => NavigateForSelected(ViewNames.Transcript), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
        ViewSectionsCommand = new DelegateCommand(() => NavigateForSelected(ViewNames.Sections), () => SelectedStudent is not null)
            .ObservesProperty(() => SelectedStudent);
    }

    public ObservableCollection<StudentSummary> Students { get; } = new();

    private int _page = 1;
    public int Page { get => _page; set => SetProperty(ref _page, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

    private string? _statusFilter;
    public string? StatusFilter { get => _statusFilter; set => SetProperty(ref _statusFilter, value); }

    private int _totalPages;
    public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private StudentSummary? _selectedStudent;
    public StudentSummary? SelectedStudent { get => _selectedStudent; set => SetProperty(ref _selectedStudent, value); }

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand NextPageCommand { get; }
    public DelegateCommand PreviousPageCommand { get; }
    public DelegateCommand WithdrawCommand { get; }
    public DelegateCommand ViewDetailCommand { get; }
    public DelegateCommand ViewLoansCommand { get; }
    public DelegateCommand ViewAccountCommand { get; }
    public DelegateCommand ViewTranscriptCommand { get; }
    public DelegateCommand ViewSectionsCommand { get; }

    public Task SearchAsync() => RunAsync(LoadStudentsCoreAsync);

    public Task WithdrawSelectedAsync() => RunAsync(async () =>
    {
        if (SelectedStudent is null)
        {
            return;
        }

        await _students.WithdrawAsync(SelectedStudent.Id);
        await LoadStudentsCoreAsync();
    });

    private async Task LoadStudentsCoreAsync()
    {
        var status = string.IsNullOrWhiteSpace(StatusFilter) ? null : StatusFilter;
        var result = await _students.SearchAsync(Page, PageSize, status);

        Students.Clear();
        foreach (var student in result.Items)
        {
            Students.Add(student);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
    }

    private void NavigateForSelected(string viewName)
    {
        if (SelectedStudent is null)
        {
            return;
        }

        _navigation.NavigateTo(viewName, new NavigationParameters { { ViewNames.StudentIdParameter, SelectedStudent.Id } });
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        if (Students.Count == 0)
        {
            _ = SearchAsync();
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
