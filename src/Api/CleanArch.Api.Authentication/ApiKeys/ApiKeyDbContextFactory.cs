using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct the
/// context at design time. The connection string here is for scaffolding only — at runtime the context
/// is configured against the real students.db connection in <c>AddApiAuthentication</c>. The history
/// table is set so generated migrations record themselves in the auth-specific table.
/// </summary>
internal sealed class ApiKeyDbContextFactory : IDesignTimeDbContextFactory<ApiKeyDbContext>
{
    public ApiKeyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApiKeyDbContext>()
            .UseSqlite(
                "Data Source=apikeys-design.db",
                sqlite => sqlite.MigrationsHistoryTable(ApiKeyDbContext.MigrationsHistoryTable))
            .Options;

        return new ApiKeyDbContext(options);
    }
}
