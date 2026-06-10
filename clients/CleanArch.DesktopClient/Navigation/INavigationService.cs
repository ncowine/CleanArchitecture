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
    public const string Books = "BooksView";
    public const string BookCopies = "BookCopiesView";
    public const string Billing = "BillingView";
    public const string Transcript = "TranscriptView";
    public const string Sections = "SectionsView";
    public const string SectionDetail = "SectionDetailView";
    public const string Courses = "CoursesView";
    public const string CourseDetail = "CourseDetailView";

    public const string StudentIdParameter = "studentId";
    public const string BookIdParameter = "bookId";
    public const string SectionIdParameter = "sectionId";
    public const string CourseIdParameter = "courseId";
}
