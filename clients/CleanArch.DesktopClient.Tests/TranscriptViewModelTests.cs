using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class TranscriptViewModelTests
{
    [Fact]
    public async Task Load_populates_gpa_standing_credits_and_entries()
    {
        var id = Guid.NewGuid();
        var api = new FakeAcademicsApiClient
        {
            Transcript = new Transcript(
                id,
                new List<TranscriptEntry>
                {
                    new("CS101", "Intro to CS", 3, "2025 Fall", "A", 4.0m),
                    new("MA101", "Calculus", 4, "2025 Fall", "B", 3.0m),
                },
                CumulativeGpa: 3.5m,
                EarnedCredits: 7,
                AttemptedCredits: 7,
                Standing: "Good"),
        };
        var vm = new TranscriptViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.Equal(3.5m, vm.CumulativeGpa);
        Assert.Equal("Good", vm.Standing);
        Assert.Equal(7, vm.EarnedCredits);
        Assert.Equal(2, vm.Entries.Count);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Back_navigates_to_students()
    {
        var navigation = new FakeNavigationService();
        var vm = new TranscriptViewModel(new FakeAcademicsApiClient(), navigation);

        vm.BackCommand.Execute();

        var (view, _) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Students, view);
    }
}
