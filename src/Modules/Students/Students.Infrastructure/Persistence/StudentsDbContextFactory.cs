using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Students.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, so the tools don't need to boot the API host. The connection string
/// here is for scaffolding only — it is never used at runtime.
/// </summary>
internal sealed class StudentsDbContextFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StudentsDbContext>()
            .UseSqlite("Data Source=students-design.db")
            .Options;

        return new StudentsDbContext(options);
    }
}
