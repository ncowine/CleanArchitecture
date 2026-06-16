using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct the
/// context at design time. It is never used at runtime — there the context is configured against the
/// real students.db connection in <c>AddApiAuthentication</c>. The connection string is read from the
/// <c>ConnectionStrings__Students</c> environment variable when set (the API-key tables live in the
/// Students database), falling back to a throwaway local SQLite file so a fresh clone can run
/// <c>dotnet ef</c> with no setup. The history table is set so generated migrations record themselves
/// in the auth-specific table.
/// </summary>
internal sealed class ApiKeyDbContextFactory : IDesignTimeDbContextFactory<ApiKeyDbContext>
{
    public ApiKeyDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Students")
            ?? "Data Source=apikeys-design.db";

        var options = new DbContextOptionsBuilder<ApiKeyDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsHistoryTable(ApiKeyDbContext.MigrationsHistoryTable))
            .Options;

        return new ApiKeyDbContext(options);
    }
}
