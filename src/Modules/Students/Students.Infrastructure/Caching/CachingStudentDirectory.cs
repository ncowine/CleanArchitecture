using Microsoft.Extensions.Caching.Hybrid;
using Students.Contracts;
using Students.Infrastructure.Contracts;

namespace Students.Infrastructure.Caching;

/// <summary>
/// Caching decorator over <see cref="StudentDirectory"/>. Student reference data is read on nearly every
/// Library request (BorrowBook, GetStudentLoans), so it's the prime caching target. Caching stays an
/// infrastructure concern — handlers and the published contract are untouched.
/// <para>
/// Uses <see cref="HybridCache.GetOrCreateAsync"/> so concurrent misses for the same student collapse
/// into a single database call (stampede protection). Invalidation on writes is handled separately by
/// <see cref="StudentCacheInvalidator"/>.
/// </para>
/// </summary>
internal sealed class CachingStudentDirectory : IStudentDirectory
{
    private readonly StudentDirectory _inner;
    private readonly HybridCache _cache;

    public CachingStudentDirectory(StudentDirectory inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            StudentCacheKeys.ForStudent(studentId),
            (inner: _inner, studentId),
            static (state, ct) => new ValueTask<StudentSummary?>(state.inner.GetAsync(state.studentId, ct)),
            cancellationToken: cancellationToken).AsTask();
}
