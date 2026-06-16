using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Students.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, so the tools don't need to boot the API host. It is never used at
/// runtime. The connection string is read from the <c>ConnectionStrings__Students</c> environment
/// variable when set (so prod/CI can scaffold against the real engine without committing the value),
/// falling back to a throwaway local SQLite file so a fresh clone can run <c>dotnet ef</c> with no setup.
/// </summary>
internal sealed class StudentsDbContextFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Students")
            ?? "Data Source=students-design.db";

        var options = new DbContextOptionsBuilder<StudentsDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new StudentsDbContext(options);
    }
}
