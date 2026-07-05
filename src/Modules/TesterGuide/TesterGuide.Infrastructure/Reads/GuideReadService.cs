using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using TesterGuide.Application.Abstractions;
using TesterGuide.Application.Actions;
using TesterGuide.Application.Configs;
using TesterGuide.Application.Content;
using TesterGuide.Application.Focuses;
using TesterGuide.Application.Templates;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Reads;

/// <summary>
/// Read projections for the Tester Guide module's own endpoints — all against its own database. Enum-to-
/// string formatting is done in memory after materialization, never pushed into SQL. Composition with the
/// primary system's content lives in the handlers (via the published catalog contract), not here.
/// </summary>
internal sealed class GuideReadService : IGuideReadService
{
    private readonly TesterGuideDbContext _db;

    public GuideReadService(TesterGuideDbContext db)
    {
        _db = db;
    }

    public async Task<GuideConfigView?> GetConfigAsync(Guid configId, CancellationToken cancellationToken)
    {
        var config = await _db.Configs.AsNoTracking()
            .Where(c => c.Id == configId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TestPlanId,
                c.TestPlanVersionId,
                c.FocusId,
                c.Mode,
                c.SyncEnabled,
                c.Status,
                c.CreatedBy,
                FocusName = _db.Focuses.Where(f => f.Id == c.FocusId).Select(f => f.Name).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            return null;
        }

        var assignmentRows = await _db.Assignments.AsNoTracking()
            .Where(a => a.GuideConfigId == configId)
            .OrderBy(a => a.AssignedOnUtc)
            .Select(a => new { a.Id, a.UserId, a.DisplayName, a.Role, a.AssignedOnUtc })
            .ToListAsync(cancellationToken);

        var assignments = assignmentRows
            .Select(a => new ConfigAssignmentView(a.Id, a.UserId, a.DisplayName, a.Role.ToString(), a.AssignedOnUtc))
            .ToList();

        return new GuideConfigView(
            config.Id,
            config.Name,
            config.TestPlanId,
            config.TestPlanVersionId,
            config.FocusId,
            config.FocusName ?? string.Empty,
            config.Mode.ToString(),
            config.SyncEnabled,
            config.Status.ToString(),
            config.CreatedBy,
            assignments);
    }

    public async Task<PagedResult<SearchGuideConfigs.ConfigListItem>> SearchConfigsAsync(
        int page, int pageSize, Guid? testPlanId, CancellationToken cancellationToken)
    {
        var query = _db.Configs.AsNoTracking();

        if (testPlanId is not null)
        {
            query = query.Where(c => c.TestPlanId == testPlanId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TestPlanId,
                c.TestPlanVersionId,
                c.Mode,
                c.SyncEnabled,
                c.Status,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SearchGuideConfigs.ConfigListItem(
                r.Id, r.Name, r.TestPlanId, r.TestPlanVersionId, r.Mode.ToString(), r.SyncEnabled, r.Status.ToString()))
            .ToList();

        return new PagedResult<SearchGuideConfigs.ConfigListItem>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<ListFocuses.FocusView>> ListFocusesAsync(CancellationToken cancellationToken) =>
        await _db.Focuses.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new ListFocuses.FocusView(f.Id, f.Name, f.Description))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ListConfigTemplates.TemplateView>> ListTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Templates.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.FocusId,
                t.Mode,
                t.SyncEnabled,
                FocusName = _db.Focuses.Where(f => f.Id == t.FocusId).Select(f => f.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ListConfigTemplates.TemplateView(
                r.Id, r.Name, r.Description, r.FocusId, r.FocusName ?? string.Empty, r.Mode.ToString(), r.SyncEnabled))
            .ToList();
    }

    public async Task<IReadOnlyList<ListContentSelections.ContentSelectionView>> ListContentAsync(
        Guid testPlanId, CancellationToken cancellationToken) =>
        await _db.ContentSelections.AsNoTracking()
            .Where(s => s.TestPlanId == testPlanId)
            .Select(s => new ListContentSelections.ContentSelectionView(s.Id, s.TestPlanId, s.TestTaskId, s.IsEnabled))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ListConfigActions.ActionView>> ListActionsAsync(
        Guid guideConfigId, CancellationToken cancellationToken)
    {
        var rows = await _db.ActionLog.AsNoTracking()
            .Where(e => e.GuideConfigId == guideConfigId)
            .OrderByDescending(e => e.OccurredOnUtc)
            .Select(e => new
            {
                e.Id,
                e.TestTaskId,
                e.PlatformId,
                e.TestPlanVersionId,
                e.Status,
                e.UserId,
                e.OccurredOnUtc,
                e.SyncState,
                e.SyncError,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(e => new ListConfigActions.ActionView(
                e.Id, e.TestTaskId, e.PlatformId, e.TestPlanVersionId,
                e.Status.ToString(), e.UserId, e.OccurredOnUtc, e.SyncState.ToString(), e.SyncError))
            .ToList();
    }
}
