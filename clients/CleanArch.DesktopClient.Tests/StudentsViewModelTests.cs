using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class StudentsViewModelTests
{
    private static StudentSummary Student(string name = "Ada Lovelace") =>
        new(Guid.NewGuid(), name, "a@uni.edu", "Active");

    [Fact]
    public async Task Search_populates_students_and_totals()
    {
        var api = new FakeStudentsApiClient
        {
            NextResult = new PagedResult<StudentSummary>(new List<StudentSummary> { Student() }, 1, 20, 5, 1),
        };
        var vm = new StudentsViewModel(api, new FakeNavigationService());

        await vm.SearchAsync();

        Assert.Single(vm.Students);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(1, vm.TotalPages);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Withdraw_selected_calls_api_and_reloads()
    {
        var student = Student();
        var api = new FakeStudentsApiClient
        {
            NextResult = new PagedResult<StudentSummary>(new List<StudentSummary> { student }, 1, 20, 1, 1),
        };
        var vm = new StudentsViewModel(api, new FakeNavigationService());
        await vm.SearchAsync();

        vm.SelectedStudent = student;
        await vm.WithdrawSelectedAsync();

        Assert.Contains(student.Id, api.Withdrawn);
        Assert.Equal(2, api.SearchCallCount); // initial load + reload after withdraw
    }

    [Fact]
    public void View_detail_navigates_with_student_id()
    {
        var student = Student();
        var navigation = new FakeNavigationService();
        var vm = new StudentsViewModel(new FakeStudentsApiClient(), navigation) { SelectedStudent = student };

        vm.ViewDetailCommand.Execute();

        var (view, parameters) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.StudentDetail, view);
        Assert.Equal(student.Id, parameters!.GetValue<Guid>(ViewNames.StudentIdParameter));
    }

    [Fact]
    public async Task Search_error_is_surfaced_and_busy_cleared()
    {
        var api = new FakeStudentsApiClient { SearchError = new InvalidOperationException("boom") };
        var vm = new StudentsViewModel(api, new FakeNavigationService());

        await vm.SearchAsync();

        Assert.Equal("boom", vm.Error);
        Assert.False(vm.IsBusy);
    }
}
