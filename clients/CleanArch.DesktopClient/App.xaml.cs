using System.Windows;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using CleanArch.DesktopClient.Views;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;

namespace CleanArch.DesktopClient;

public partial class App : PrismApplication
{
    // Where the API is hosted. For a real client this would come from config/per-environment.
    private const string ApiBaseUrl = "http://localhost:5080/";

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // One long-lived authenticated HttpClient + the token store, built by the API client library.
        var (tokens, students, library) = ApiClientFactory.Create(ApiBaseUrl);
        containerRegistry.RegisterInstance<ITokenStore>(tokens);
        containerRegistry.RegisterInstance<IStudentsApiClient>(students);
        containerRegistry.RegisterInstance<ILibraryApiClient>(library);

        containerRegistry.RegisterSingleton<INavigationService, RegionNavigator>();

        containerRegistry.RegisterForNavigation<LoginView, LoginViewModel>();
        containerRegistry.RegisterForNavigation<StudentsView, StudentsViewModel>();
        containerRegistry.RegisterForNavigation<StudentDetailView, StudentDetailViewModel>();
        containerRegistry.RegisterForNavigation<LoansView, LoansViewModel>();
    }

    protected override void OnInitialized()
    {
        var regionManager = Container.Resolve<IRegionManager>();
        regionManager.RequestNavigate(RegionNavigator.ContentRegion, ViewNames.Login);
        base.OnInitialized();
    }
}
