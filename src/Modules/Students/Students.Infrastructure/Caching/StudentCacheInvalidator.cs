using Microsoft.Extensions.Caching.Hybrid;
using Students.Application.Abstractions;

namespace Students.Infrastructure.Caching;

internal sealed class StudentCacheInvalidator : IStudentCacheInvalidator
{
    private readonly HybridCache _cache;

    public StudentCacheInvalidator(HybridCache cache)
    {
        _cache = cache;
    }

    public Task RemoveAsync(Guid studentId, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(StudentCacheKeys.ForStudent(studentId), cancellationToken).AsTask();
}
