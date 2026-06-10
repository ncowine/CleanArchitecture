using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>
/// A section with its roster, plus enrolment actions for the student in context: enrol (seat or
/// waitlist), drop, record a grade, and cancel the section.
/// </summary>
public sealed class SectionDetailViewModel : ViewModelBase, INavigationAware
{
    private readonly IAcademicsApiClient _academics;
    private readonly INavigationService _navigation;

    public SectionDetailViewModel(IAcademicsApiClient academics, INavigationService navigation)
    {
        _academics = academics;
        _navigation = navigation;

        EnrollCommand = new DelegateCommand(async () => await EnrollAsync(), () => Section is not null)
            .ObservesProperty(() => Section);
        DropCommand = new DelegateCommand(async () => await DropAsync(), () => Section is not null)
            .ObservesProperty(() => Section);
        RecordGradeCommand = new DelegateCommand(async () => await RecordGradeAsync(),
                () => Section is not null && !string.IsNullOrWhiteSpace(Grade))
            .ObservesProperty(() => Section).ObservesProperty(() => Grade);
        CancelSectionCommand = new DelegateCommand(async () => await CancelSectionAsync(), () => Section is not null)
            .ObservesProperty(() => Section);
        BackCommand = new DelegateCommand(() =>
            _navigation.NavigateTo(ViewNames.Sections, new NavigationParameters { { ViewNames.StudentIdParameter, StudentId } }));
    }

    public ObservableCollection<RosterEntry> Roster { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private Guid _sectionId;
    public Guid SectionId { get => _sectionId; private set => SetProperty(ref _sectionId, value); }

    private SectionDetail? _section;
    public SectionDetail? Section { get => _section; private set => SetProperty(ref _section, value); }

    private string _grade = string.Empty;
    public string Grade { get => _grade; set => SetProperty(ref _grade, value); }

    private string? _notice;
    public string? Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public DelegateCommand EnrollCommand { get; }
    public DelegateCommand DropCommand { get; }
    public DelegateCommand RecordGradeCommand { get; }
    public DelegateCommand CancelSectionCommand { get; }
    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId, Guid sectionId)
    {
        StudentId = studentId;
        SectionId = sectionId;
        return RunAsync(LoadCoreAsync);
    }

    public Task EnrollAsync() => RunAsync(async () =>
    {
        var result = await _academics.EnrollAsync(SectionId, StudentId);
        Notice = result.WaitlistPosition is { } position
            ? $"Waitlisted at position {position}."
            : $"Enrolled ({result.Status}).";
        await LoadCoreAsync();
    });

    public Task DropAsync() => RunAsync(async () =>
    {
        await _academics.DropAsync(SectionId, StudentId);
        Notice = "Dropped.";
        await LoadCoreAsync();
    });

    public Task RecordGradeAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Grade))
        {
            return;
        }

        var result = await _academics.RecordGradeAsync(SectionId, StudentId, Grade);
        Notice = $"Recorded grade {result.Grade} ({result.Points:0.0}).";
        Grade = string.Empty;
        await LoadCoreAsync();
    });

    public Task CancelSectionAsync() => RunAsync(async () =>
    {
        await _academics.CancelSectionAsync(SectionId);
        Notice = "Section cancelled.";
        await LoadCoreAsync();
    });

    private async Task LoadCoreAsync()
    {
        Section = await _academics.GetSectionAsync(SectionId);

        Roster.Clear();
        if (Section is null)
        {
            return;
        }

        var roster = await _academics.GetRosterAsync(SectionId);
        foreach (var entry in roster.Items)
        {
            Roster.Add(entry);
        }
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        var hasStudent = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId);
        var hasSection = navigationContext.Parameters.TryGetValue<Guid>(ViewNames.SectionIdParameter, out var sectionId);
        if (hasStudent && hasSection)
        {
            _ = LoadAsync(studentId, sectionId);
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
