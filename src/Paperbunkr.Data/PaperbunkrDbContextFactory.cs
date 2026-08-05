using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Paperbunkr.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> / <c>dotnet ef database update</c> work
/// standalone against Paperbunkr.Data without needing Paperbunkr.App (which owns DI/composition at
/// runtime) to build or run. Points at the same default SQLite file convention the app will use
/// (%AppData%\Paperbunkr\paperbunkr.db) rather than ":memory:", so migrations are generated/tested
/// against the real provider behavior.
/// </summary>
public class PaperbunkrDbContextFactory : IDesignTimeDbContextFactory<PaperbunkrDbContext>
{
    public PaperbunkrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaperbunkrDbContext>();
        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new PaperbunkrDbContext(optionsBuilder.Options);
    }
}
