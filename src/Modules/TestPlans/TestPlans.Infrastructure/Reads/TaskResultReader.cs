using Microsoft.EntityFrameworkCore;
using TestPlans.Contracts;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Reads;

/// <summary>Implements the published <see cref="ITaskResultReader"/> against the Test Plans database.</summary>
internal sealed class TaskResultReader : ITaskResultReader
{
    private readonly TestPlansDbContext _db;

    public TaskResultReader(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task<TaskStatusSnapshot?> GetAsync(
        Guid testTaskId, Guid platformId, Guid versionId, CancellationToken cancellationToken)
    {
        var result = await _db.TaskResults.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TestTaskId == testTaskId && r.PlatformId == platformId && r.TestPlanVersionId == versionId,
                cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new TaskStatusSnapshot(
            result.TestTaskId,
            result.PlatformId,
            result.TestPlanVersionId,
            result.Status.ToString(),
            result.ActorId,
            result.ActionedOnUtc);
    }
}
