namespace Students.Infrastructure.Caching;

/// <summary>Single source of truth for student cache keys, shared by the reader and the invalidator.</summary>
internal static class StudentCacheKeys
{
    public static string ForStudent(Guid studentId) => $"students:{studentId}";
}
