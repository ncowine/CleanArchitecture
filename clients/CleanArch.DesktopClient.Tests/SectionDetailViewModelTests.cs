using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class SectionDetailViewModelTests
{
    private static SectionDetail Section() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "CS101", "Intro to CS", Guid.NewGuid(), "Dr. Ada",
            "2025 Fall", "001", 30, 12, 0, "Open",
            new SectionSchedule("MWF", new TimeOnly(9, 0), new TimeOnly(9, 50), "Hall A"));

    private static PagedResult<RosterEntry> Roster(params RosterEntry[] entries) =>
        new(entries, 1, 20, entries.Length, entries.Length == 0 ? 0 : 1);

    private static FakeAcademicsApiClient ApiWithSection(SectionDetail? section = null) =>
        new() { Section = section ?? Section(), RosterResult = Roster(new RosterEntry(Guid.NewGuid(), "Bob", "Enrolled", null, new DateOnly(2025, 9, 1))) };

    [Fact]
    public async Task Load_populates_section_and_roster()
    {
        var vm = new SectionDetailViewModel(ApiWithSection(), new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(vm.Section);
        Assert.Single(vm.Roster);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Enroll_calls_api_for_section_and_student_and_reloads()
    {
        var studentId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var api = ApiWithSection();
        var vm = new SectionDetailViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId, sectionId);

        await vm.EnrollAsync();

        var enrollment = Assert.Single(api.Enrollments);
        Assert.Equal(sectionId, enrollment.sectionId);
        Assert.Equal(studentId, enrollment.studentId);
        Assert.Equal(2, api.SectionsRequested.Count); // initial load + reload after enroll
        Assert.Contains("Enrolled", vm.Notice);
    }

    [Fact]
    public async Task Enroll_reports_waitlist_position_when_waitlisted()
    {
        var api = ApiWithSection();
        api.EnrollResult = new EnrollResult(Guid.NewGuid(), "Waitlisted", 3);
        var vm = new SectionDetailViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        await vm.EnrollAsync();

        Assert.Contains("position 3", vm.Notice);
    }

    [Fact]
    public async Task Drop_calls_api_and_reloads()
    {
        var studentId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var api = ApiWithSection();
        var vm = new SectionDetailViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId, sectionId);

        await vm.DropAsync();

        var drop = Assert.Single(api.Drops);
        Assert.Equal(sectionId, drop.sectionId);
        Assert.Equal(studentId, drop.studentId);
    }

    [Fact]
    public async Task Record_grade_requires_a_grade_then_calls_api_and_clears_it()
    {
        var sectionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var api = ApiWithSection();
        var vm = new SectionDetailViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId, sectionId);

        Assert.False(vm.RecordGradeCommand.CanExecute()); // no grade yet

        vm.Grade = "A";
        Assert.True(vm.RecordGradeCommand.CanExecute());
        await vm.RecordGradeAsync();

        var grade = Assert.Single(api.Grades);
        Assert.Equal(sectionId, grade.sectionId);
        Assert.Equal(studentId, grade.studentId);
        Assert.Equal("A", grade.grade);
        Assert.Equal(string.Empty, vm.Grade); // cleared after recording
    }

    [Fact]
    public async Task Cancel_section_calls_api()
    {
        var sectionId = Guid.NewGuid();
        var api = ApiWithSection();
        var vm = new SectionDetailViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid(), sectionId);

        await vm.CancelSectionAsync();

        Assert.Equal(sectionId, Assert.Single(api.CancelledSections));
    }

    [Fact]
    public void Actions_are_disabled_until_a_section_is_loaded()
    {
        var vm = new SectionDetailViewModel(new FakeAcademicsApiClient(), new FakeNavigationService());

        Assert.False(vm.EnrollCommand.CanExecute());
        Assert.False(vm.DropCommand.CanExecute());
        Assert.False(vm.CancelSectionCommand.CanExecute());
    }

    [Fact]
    public async Task Back_navigates_to_sections_with_the_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new SectionDetailViewModel(ApiWithSection(), navigation);
        await vm.LoadAsync(studentId, Guid.NewGuid());

        vm.BackCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Sections, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }
}
