using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TestPlans.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct the
/// context at design time, so the tools don't need to boot the API host. Never used at runtime. The
/// connection string is read from <c>ConnectionStrings__TestPlans</c> when set, falling back to a throwaway
/// local SQLite file so a fresh clone can run <c>dotnet ef</c> with no setup.
/// </summary>
internal sealed class TestPlansDbContextFactory : IDesignTimeDbContextFactory<TestPlansDbContext>
{
    public TestPlansDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TestPlans")
            ?? "Data Source=testplans-design.db";

        var options = new DbContextOptionsBuilder<TestPlansDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new TestPlansDbContext(options);
    }
}
