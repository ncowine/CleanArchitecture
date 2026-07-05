using BuildingBlocks.Pagination;
using TesterGuide.Application.Actions;
using TesterGuide.Application.Configs;
using TesterGuide.Application.Content;
using TesterGuide.Application.Focuses;
using TesterGuide.Application.Templates;

namespace TesterGuide.Application.Abstractions;

/// <summary>An assigned user, as read from the Tester Guide database.</summary>
public sealed record ConfigAssignmentView(Guid Id, string UserId, string DisplayName, string Role, DateTime AssignedOnUtc);

/// <summary>
/// A guide config as held in the Tester Guide database (with its focus name and assignments resolved), but
/// <b>before</b> composition with the primary system's content tree. <see cref="Configs.GetGuideConfig"/>
/// wraps this with the tree and version label read via the published catalog contract.
/// </summary>
public sealed record GuideConfigView(
    Guid Id,
    string Name,
    Guid TestPlanId,
    Guid TestPlanVersionId,
    Guid FocusId,
    string FocusName,
    string Mode,
    bool SyncEnabled,
    string Status,
    string CreatedBy,
    IReadOnlyList<ConfigAssignmentView> Assignments);

/// <summary>Read projections for the Tester Guide module's own endpoints (all against its own database).</summary>
public interface IGuideReadService
{
    Task<GuideConfigView?> GetConfigAsync(Guid configId, CancellationToken cancellationToken);

    Task<PagedResult<SearchGuideConfigs.ConfigListItem>> SearchConfigsAsync(
        int page, int pageSize, Guid? testPlanId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ListFocuses.FocusView>> ListFocusesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ListConfigTemplates.TemplateView>> ListTemplatesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ListContentSelections.ContentSelectionView>> ListContentAsync(
        Guid testPlanId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ListConfigActions.ActionView>> ListActionsAsync(
        Guid guideConfigId, CancellationToken cancellationToken);
}
