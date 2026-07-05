using BuildingBlocks.Messaging;
using TestPlans.Contracts;

namespace TestPlans.Application.Reads;

/// <summary>Read a task's current status for a (platform, version) from the primary source of truth.</summary>
public static class GetTaskStatus
{
    public sealed record Query(Guid TestTaskId, Guid PlatformId, Guid TestPlanVersionId)
        : IRequest<TaskStatusSnapshot?>;

    public sealed class Handler : IRequestHandler<Query, TaskStatusSnapshot?>
    {
        private readonly ITaskResultReader _results;

        public Handler(ITaskResultReader results)
        {
            _results = results;
        }

        public Task<TaskStatusSnapshot?> Handle(Query query, CancellationToken cancellationToken) =>
            _results.GetAsync(query.TestTaskId, query.PlatformId, query.TestPlanVersionId, cancellationToken);
    }
}
