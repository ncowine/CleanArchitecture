using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

public sealed class StudentDetailViewModel : ViewModelBase, INavigationAware
{
    private readonly IStudentsApiClient _students;
    private readonly INavigationService _navigation;

    public StudentDetailViewModel(IStudentsApiClient students, INavigationService navigation)
    {
        _students = students;
        _navigation = navigation;
        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    private StudentDetail? _detail;
    public StudentDetail? Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId) =>
        RunAsync(async () => Detail = await _students.GetDetailAsync(studentId));

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
