using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Equipment.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, so the tools don't need to boot the API host. It is never used at
/// runtime. The connection string is read from the <c>ConnectionStrings__Equipment</c> environment
/// variable when set, falling back to a throwaway local SQLite file so a fresh clone can run
/// <c>dotnet ef</c> with no setup.
/// </summary>
internal sealed class EquipmentDbContextFactory : IDesignTimeDbContextFactory<EquipmentDbContext>
{
    public EquipmentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Equipment")
            ?? "Data Source=equipment-design.db";

        var options = new DbContextOptionsBuilder<EquipmentDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new EquipmentDbContext(options);
    }
}
