using BuildingBlocks.Messaging;
using TesterGuide.Application.Abstractions;
using TestPlans.Contracts;

namespace TesterGuide.Application.Configs;

/// <summary>
/// Render a guide config: its own data (focus, assignments) from the Tester Guide database, composed in the
/// application layer with the primary system's content tree and version label read through the published
/// <see cref="ITestPlanCatalog"/> contract. Two databases, joined in code — never in SQL.
/// </summary>
public static class GetGuideConfig
{
    public sealed record AssignmentDto(Guid Id, string UserId, string DisplayName, string Role, DateTime AssignedOnUtc);

    public sealed record Response(
        Guid Id,
        string Name,
        Guid TestPlanId,
        string TestPlanCode,
        Guid TestPlanVersionId,
        string VersionLabel,
        Guid FocusId,
        string FocusName,
        string Mode,
        bool SyncEnabled,
        string Status,
        string CreatedBy,
        IReadOnlyList<AssignmentDto> Assignments,
        TestPlanTree? Content);

    public sealed record Query(Guid ConfigId) : IRequest<Response?>;

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IGuideReadService _reads;
        private readonly ITestPlanCatalog _catalog;

        public Handler(IGuideReadService reads, ITestPlanCatalog catalog)
        {
            _reads = reads;
            _catalog = catalog;
        }

        public async Task<Response?> Handle(Query query, CancellationToken cancellationToken)
        {
            var view = await _reads.GetConfigAsync(query.ConfigId, cancellationToken);
            if (view is null)
            {
                return null;
            }

            // Cross-module composition via the published contract.
            var tree = await _catalog.GetTreeAsync(view.TestPlanId, cancellationToken);
            var versions = await _catalog.GetVersionsAsync(view.TestPlanId, cancellationToken);
            var versionLabel = versions.FirstOrDefault(v => v.Id == view.TestPlanVersionId)?.Label ?? string.Empty;

            var assignments = view.Assignments
                .Select(a => new AssignmentDto(a.Id, a.UserId, a.DisplayName, a.Role, a.AssignedOnUtc))
                .ToList();

            return new Response(
                view.Id,
                view.Name,
                view.TestPlanId,
                tree?.Code ?? string.Empty,
                view.TestPlanVersionId,
                versionLabel,
                view.FocusId,
                view.FocusName,
                view.Mode,
                view.SyncEnabled,
                view.Status,
                view.CreatedBy,
                assignments,
                tree);
        }
    }
}
