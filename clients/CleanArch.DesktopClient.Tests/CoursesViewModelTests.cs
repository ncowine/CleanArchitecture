using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class CoursesViewModelTests
{
    private static CourseListItem Course() => new(Guid.NewGuid(), "CS101", "Intro to CS", 3, "Computer Science");

    private static PagedResult<CourseListItem> Paged(params CourseListItem[] courses) =>
        new(courses, 1, 20, courses.Length, courses.Length == 0 ? 0 : 1);

    [Fact]
    public async Task Load_searches_and_populates_totals()
    {
        var api = new FakeAcademicsApiClient { CoursesResult = new PagedResult<CourseListItem>(new[] { Course() }, 1, 20, 9, 1) };
        var vm = new CoursesViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid());

        Assert.Single(vm.Courses);
        Assert.Equal(9, vm.TotalCount);
    }

    [Fact]
    public async Task Search_passes_department_and_blank_becomes_null()
    {
        var api = new FakeAcademicsApiClient { CoursesResult = Paged() };
        var vm = new CoursesViewModel(api, new FakeNavigationService()) { DepartmentFilter = "  " };

        await vm.SearchAsync();

        var (_, _, department) = Assert.Single(api.CourseSearches);
        Assert.Null(department);
    }

    [Fact]
    public async Task View_detail_navigates_with_student_and_course_ids()
    {
        var studentId = Guid.NewGuid();
        var course = Course();
        var navigation = new FakeNavigationService();
        var vm = new CoursesViewModel(new FakeAcademicsApiClient { CoursesResult = Paged(course) }, navigation);
        await vm.LoadAsync(studentId);

        vm.SelectedCourse = course;
        vm.ViewDetailCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.CourseDetail, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
        Assert.Equal(course.Id, parameters.GetValue<Guid>(ViewNames.CourseIdParameter));
    }

    [Fact]
    public async Task Back_navigates_to_sections_with_the_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new CoursesViewModel(new FakeAcademicsApiClient { CoursesResult = Paged() }, navigation);
        await vm.LoadAsync(studentId);

        vm.BackCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Sections, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }
}
