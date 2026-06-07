using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Library.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, so the tools don't need to boot the API host. The connection string
/// here is for scaffolding only — it is never used at runtime.
/// </summary>
internal sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite("Data Source=library-design.db")
            .Options;

        return new LibraryDbContext(options);
    }
}
