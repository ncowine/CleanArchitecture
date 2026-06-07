using Prism.Regions;

namespace CleanArch.DesktopClient.Navigation;

/// <summary>
/// Thin navigation seam over Prism's region manager so ViewModels stay testable — tests fake this one
/// method instead of the large IRegionManager surface.
/// </summary>
public interface INavigationService
{
    void NavigateTo(string viewName, NavigationParameters? parameters = null);
}

internal sealed class RegionNavigator : INavigationService
{
    public const string ContentRegion = "ContentRegion";

    private readonly IRegionManager _regions;
    public RegionNavigator(IRegionManager regions) => _regions = regions;

    public void NavigateTo(string viewName, NavigationParameters? parameters = null) =>
        _regions.RequestNavigate(ContentRegion, viewName, parameters ?? new NavigationParameters());
}

/// <summary>View names used for navigation (match the registered view type names).</summary>
public static class ViewNames
{
    public const string Login = "LoginView";
    public const string Students = "StudentsView";
    public const string StudentDetail = "StudentDetailView";
    public const string Loans = "LoansView";

    public const string StudentIdParameter = "studentId";
}
