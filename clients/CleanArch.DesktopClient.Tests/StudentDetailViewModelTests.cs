using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class StudentDetailViewModelTests
{
    [Fact]
    public async Task Load_sets_detail()
    {
        var id = Guid.NewGuid();
        var api = new FakeStudentsApiClient
        {
            Detail = new StudentDetail(id, "Ada", "Lovelace", "a@uni.edu", "Active",
                Address: null,
                EmergencyContacts: new List<EmergencyContactDto>(),
                Enrollments: new List<EnrollmentDto>(),
                ActiveEnrollments: 0),
        };
        var vm = new StudentDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.NotNull(vm.Detail);
        Assert.Equal("Ada", vm.Detail!.FirstName);
        Assert.False(vm.IsBusy);
    }

    private static StudentDetail Detail(Guid id) =>
        new(id, "Ada", "Lovelace", "a@uni.edu", "Active",
            Address: null,
            EmergencyContacts: new List<EmergencyContactDto>(),
            Enrollments: new List<EnrollmentDto>(),
            ActiveEnrollments: 0);

    [Fact]
    public async Task Load_populates_holds_and_sets_the_flag()
    {
        var id = Guid.NewGuid();
        var api = new FakeStudentsApiClient
        {
            Detail = Detail(id),
            HoldsResult = new PagedResult<StudentHold>(
                new List<StudentHold> { new(Guid.NewGuid(), "Library fine exceeds limit", new DateTime(2026, 5, 1)) },
                1, 20, 1, 1),
        };
        var vm = new StudentDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.Single(vm.Holds);
        Assert.Equal(1, vm.HoldCount);
        Assert.True(vm.HasHolds);
    }

    [Fact]
    public async Task No_holds_leaves_the_flag_clear()
    {
        var id = Guid.NewGuid();
        var api = new FakeStudentsApiClient { Detail = Detail(id) };
        var vm = new StudentDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(id);

        Assert.Empty(vm.Holds);
        Assert.False(vm.HasHolds);
    }

    [Fact]
    public async Task A_missing_student_skips_the_holds_lookup()
    {
        var api = new FakeStudentsApiClient
        {
            Detail = null,
            HoldsResult = new PagedResult<StudentHold>(
                new List<StudentHold> { new(Guid.NewGuid(), "stale", new DateTime(2026, 5, 1)) }, 1, 20, 1, 1),
        };
        var vm = new StudentDetailViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid());

        Assert.Null(vm.Detail);
        Assert.Empty(vm.Holds); // not loaded for a student that doesn't exist
        Assert.False(vm.HasHolds);
    }
}
