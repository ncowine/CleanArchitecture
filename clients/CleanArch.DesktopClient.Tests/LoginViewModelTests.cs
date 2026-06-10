using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class LoginViewModelTests
{
    [Fact]
    public async Task SignIn_signs_in_then_navigates_to_students()
    {
        var session = new FakeAuthSession();
        var navigation = new FakeNavigationService();
        var vm = new LoginViewModel(session, navigation) { Actor = "registrar@uni" };

        await vm.SignInAsync();

        Assert.True(session.IsSignedIn);
        Assert.Contains("registrar@uni", session.SignIns);
        Assert.Contains(navigation.Navigations, n => n.View == ViewNames.Students);
        Assert.Null(vm.Error);
    }
}
