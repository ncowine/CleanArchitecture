using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class CourseDetailViewModelTests
{
    [Fact]
    public async Task Load_sets_course_with_prerequisites()
    {
        var courseId = Guid.NewGuid();
        var api = new FakeAcademicsApiClient
        {
            Course = new CourseDetail(courseId, "CS201", "Data Structures", "Trees and graphs", 3, "Computer Science",
                new List<CoursePrerequisite> { new(Guid.NewGuid(), "CS101", "Intro to CS") }),
        };
        var vm = new CourseDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid(), courseId);

        Assert.NotNull(vm.Course);
        Assert.Equal("CS201", vm.Course!.Code);
        Assert.Single(vm.Course.Prerequisites);
    }

    [Fact]
    public async Task Load_leaves_course_null_when_not_found()
    {
        var api = new FakeAcademicsApiClient { Course = null };
        var vm = new CourseDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(vm.Course);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Back_navigates_to_courses_with_the_student()
    {
        var studentId = Guid.NewGuid();
        var navigation = new FakeNavigationService();
        var vm = new CourseDetailViewModel(new FakeAcademicsApiClient(), navigation);
        await vm.LoadAsync(studentId, Guid.NewGuid());

        vm.BackCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Courses, view);
        Assert.Equal(studentId, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }
}
