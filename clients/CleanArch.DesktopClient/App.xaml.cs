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

    // Service API key the API authorizes this client by (X-Api-Key). This is the well-known seeded dev
    // key; a real client would read a provisioned key from config/secret storage, not hard-code it.
    private const string ApiKey = "dev-api-key-integration";

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // One long-lived authenticated HttpClient + the auth session, built by the API client library.
        var (session, students, library, billing, academics) = ApiClientFactory.Create(ApiBaseUrl, ApiKey);
        containerRegistry.RegisterInstance<IAuthSession>(session);
        containerRegistry.RegisterInstance<IStudentsApiClient>(students);
        containerRegistry.RegisterInstance<ILibraryApiClient>(library);
        containerRegistry.RegisterInstance<IBillingApiClient>(billing);
        containerRegistry.RegisterInstance<IAcademicsApiClient>(academics);

        containerRegistry.RegisterSingleton<INavigationService, RegionNavigator>();

        containerRegistry.RegisterForNavigation<LoginView, LoginViewModel>();
        containerRegistry.RegisterForNavigation<StudentsView, StudentsViewModel>();
        containerRegistry.RegisterForNavigation<StudentDetailView, StudentDetailViewModel>();
        containerRegistry.RegisterForNavigation<LoansView, LoansViewModel>();
        containerRegistry.RegisterForNavigation<BooksView, BooksViewModel>();
        containerRegistry.RegisterForNavigation<BookCopiesView, BookCopiesViewModel>();
        containerRegistry.RegisterForNavigation<BillingView, BillingViewModel>();
        containerRegistry.RegisterForNavigation<TranscriptView, TranscriptViewModel>();
        containerRegistry.RegisterForNavigation<SectionsView, SectionsViewModel>();
        containerRegistry.RegisterForNavigation<SectionDetailView, SectionDetailViewModel>();
        containerRegistry.RegisterForNavigation<CoursesView, CoursesViewModel>();
        containerRegistry.RegisterForNavigation<CourseDetailView, CourseDetailViewModel>();
    }

    protected override void OnInitialized()
    {
        var regionManager = Container.Resolve<IRegionManager>();
        regionManager.RequestNavigate(RegionNavigator.ContentRegion, ViewNames.Login);
        base.OnInitialized();
    }
}
