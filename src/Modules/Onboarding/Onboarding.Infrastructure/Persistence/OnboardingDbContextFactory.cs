using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Onboarding.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, so the tools don't need to boot the API host. It is never used at
/// runtime. The connection string is read from the <c>ConnectionStrings__Onboarding</c> environment
/// variable when set, falling back to a throwaway local SQLite file so a fresh clone can run
/// <c>dotnet ef</c> with no setup.
/// </summary>
internal sealed class OnboardingDbContextFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    public OnboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Onboarding")
            ?? "Data Source=onboarding-design.db";

        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new OnboardingDbContext(options);
    }
}
