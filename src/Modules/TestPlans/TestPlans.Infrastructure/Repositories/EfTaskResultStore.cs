using Microsoft.EntityFrameworkCore;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Repositories;

internal sealed class EfTaskResultStore : ITaskResultStore
{
    private readonly TestPlansDbContext _db;

    public EfTaskResultStore(TestPlansDbContext db)
    {
        _db = db;
    }

    public async Task RecordAsync(
        Guid actionId,
        Guid testTaskId,
        Guid platformId,
        Guid versionId,
        TaskResultStatus status,
        string actorId,
        ActionSource source,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken)
    {
        // Upsert the current status for this (task, platform, version).
        var result = await _db.TaskResults.FirstOrDefaultAsync(
            r => r.TestTaskId == testTaskId && r.PlatformId == platformId && r.TestPlanVersionId == versionId,
            cancellationToken);

        if (result is null)
        {
            result = TaskResult.Create(testTaskId, platformId, versionId, status, actorId, occurredOnUtc);
            await _db.TaskResults.AddAsync(result, cancellationToken);
        }
        else
        {
            result.Update(status, actorId, occurredOnUtc);
        }

        // Append the immutable history entry.
        var entry = ActionLogEntry.Create(
            actionId, testTaskId, platformId, versionId, status, actorId, occurredOnUtc, source);
        await _db.ActionLog.AddAsync(entry, cancellationToken);
    }
}
