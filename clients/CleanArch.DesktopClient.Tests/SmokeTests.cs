using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class SmokeTests
{
    [Fact]
    public void MainWindowViewModel_has_a_title()
    {
        var viewModel = new MainWindowViewModel();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Title));
    }
}
