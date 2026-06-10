using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class SectionsViewModelTests
{
    private static SectionListItem Section(string code = "CS101") =>
        new(Guid.NewGuid(), code, "Intro to CS", "2025 Fall", "001", "Dr. Ada", 30, 12, "Open");

    private static PagedResult<SectionListItem> Paged(params SectionListItem[] sections) =>
        new(sections, 1, 20, sections.Length, sections.Length == 0 ? 0 : 1);

    [Fact]
    public async Task Load_searches_for_student_and_populates_totals()
    {
        var studentId = Guid.NewGuid();
        var api = new FakeAcademicsApiClient { SectionsResult = new PagedResult<SectionListItem>(new[] { Section() }, 1, 20, 3, 1) };
        var vm = new SectionsViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(studentId);

        Assert.Equal(studentId, vm.StudentId);
        Assert.Single(vm.Sections);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.TotalPages);
    }

    [Fact]
    public async Task Search_passes_term_and_blank_becomes_null()
    {
        var api = new FakeAcademicsApiClient { SectionsResult = Paged() };
        var vm = new SectionsViewModel(api, new FakeNavigationService()) { TermFilter = "   " };

        await vm.SearchAsync();

        var (_, _, term) = Assert.Single(api.SectionSearches);
        Assert.Null(term);
    }

    [Fact]
    public async Task View_detail_navigates_with_student_and_section_ids()
    {
        var studentId = Guid.NewGuid();
        var section = Section();
        var navigation = new FakeNavigationService();
        var vm = new SectionsViewModel(new FakeAcademicsApiClient { SectionsResult = Paged(section) }, navigation);
        await vm.LoadAsync(studentId);

        vm.SelectedSection = section;
        vm.ViewDetailCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.SectionDetail, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
        Assert.Equal(section.Id, parameters.GetValue<Guid>(ViewNames.SectionIdParameter));
    }

    [Fact]
    public async Task Browse_courses_navigates_with_the_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new SectionsViewModel(new FakeAcademicsApiClient { SectionsResult = Paged() }, navigation);
        await vm.LoadAsync(studentId);

        vm.BrowseCoursesCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Courses, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }

    [Fact]
    public void View_detail_is_disabled_without_a_selection()
    {
        var vm = new SectionsViewModel(new FakeAcademicsApiClient(), new FakeNavigationService());

        Assert.False(vm.ViewDetailCommand.CanExecute());
    }
}
