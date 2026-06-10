using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Read-only course detail with its prerequisite list.</summary>
public sealed class CourseDetailViewModel : ViewModelBase, INavigationAware
{
    private readonly IAcademicsApiClient _academics;
    private readonly INavigationService _navigation;

    public CourseDetailViewModel(IAcademicsApiClient academics, INavigationService navigation)
    {
        _academics = academics;
        _navigation = navigation;
        BackCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Courses, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
    }

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private CourseDetail? _course;
    public CourseDetail? Course { get => _course; private set => SetProperty(ref _course, value); }

    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId, Guid courseId)
    {
        StudentId = studentId;
        return RunAsync(async () => Course = await _academics.GetCourseAsync(courseId));
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        var hasStudent = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId);
        var hasCourse = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.CourseIdParameter, out var courseId);
        if (hasStudent && hasCourse)
        {
            _ = LoadAsync(studentId, courseId);
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
