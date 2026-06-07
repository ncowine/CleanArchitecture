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
}
