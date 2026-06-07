using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class LoginViewModelTests
{
    [Fact]
    public async Task SignIn_signs_in_then_navigates_to_students()
    {
        var tokens = new FakeTokenStore();
        var navigation = new FakeNavigationService();
        var vm = new LoginViewModel(tokens, navigation) { Actor = "registrar@uni" };

        await vm.SignInAsync();

        Assert.True(tokens.IsSignedIn);
        Assert.Contains("registrar@uni", tokens.SignIns);
        Assert.Contains(navigation.Navigations, n => n.View == ViewNames.Students);
        Assert.Null(vm.Error);
    }
}
