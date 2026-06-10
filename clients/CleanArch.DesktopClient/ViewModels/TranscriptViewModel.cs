using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Read-only academic transcript: graded courses, GPA, credits, and standing.</summary>
public sealed class TranscriptViewModel : ViewModelBase, INavigationAware
{
    private readonly IAcademicsApiClient _academics;
    private readonly INavigationService _navigation;

    public TranscriptViewModel(IAcademicsApiClient academics, INavigationService navigation)
    {
        _academics = academics;
        _navigation = navigation;
        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    public ObservableCollection<TranscriptEntry> Entries { get; } = new();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private decimal _cumulativeGpa;
    public decimal CumulativeGpa { get => _cumulativeGpa; private set => SetProperty(ref _cumulativeGpa, value); }

    private int _earnedCredits;
    public int EarnedCredits { get => _earnedCredits; private set => SetProperty(ref _earnedCredits, value); }

    private int _attemptedCredits;
    public int AttemptedCredits { get => _attemptedCredits; private set => SetProperty(ref _attemptedCredits, value); }

    private string? _standing;
    public string? Standing { get => _standing; private set => SetProperty(ref _standing, value); }

    public DelegateCommand BackCommand { get; }

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        return RunAsync(async () =>
        {
            var transcript = await _academics.GetTranscriptAsync(studentId);
            CumulativeGpa = transcript.CumulativeGpa;
            EarnedCredits = transcript.EarnedCredits;
            AttemptedCredits = transcript.AttemptedCredits;
            Standing = transcript.Standing;

            Entries.Clear();
            foreach (var entry in transcript.Entries)
            {
                Entries.Add(entry);
            }
        });
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
