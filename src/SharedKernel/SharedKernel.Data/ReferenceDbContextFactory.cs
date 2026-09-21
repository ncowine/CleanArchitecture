using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SharedKernel.Data;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct the
/// context at design time, so the tools don't need to boot the API host. It is never used at runtime. The
/// connection string is read from the <c>ConnectionStrings__Reference</c> environment variable when set,
/// falling back to a throwaway local SQLite file so a fresh clone can run <c>dotnet ef</c> with no setup.
/// </summary>
internal sealed class ReferenceDbContextFactory : IDesignTimeDbContextFactory<ReferenceDbContext>
{
    public ReferenceDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Reference")
            ?? "Data Source=reference-design.db";

        var options = new DbContextOptionsBuilder<ReferenceDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ReferenceDbContext(options);
    }
}
