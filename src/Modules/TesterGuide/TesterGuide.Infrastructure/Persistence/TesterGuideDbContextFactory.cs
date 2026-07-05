using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TesterGuide.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools at design time, so the tools don't need to boot the API
/// host. Never used at runtime. The connection string is read from <c>ConnectionStrings__TesterGuide</c>
/// when set, falling back to a throwaway local SQLite file so a fresh clone can run <c>dotnet ef</c> with
/// no setup.
/// </summary>
internal sealed class TesterGuideDbContextFactory : IDesignTimeDbContextFactory<TesterGuideDbContext>
{
    public TesterGuideDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TesterGuide")
            ?? "Data Source=testerguide-design.db";

        var options = new DbContextOptionsBuilder<TesterGuideDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new TesterGuideDbContext(options);
    }
}
