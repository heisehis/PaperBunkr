using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// App-side access to the real Paperbunkr library database (docs/onboarding.md §5), replacing
/// the ViewModels' earlier in-memory sample data. Each call to <see cref="CreateContext"/> opens
/// a fresh short-lived <see cref="PaperbunkrDbContext"/> - EF Core contexts aren't meant to be
/// held/shared long-term, and this app has no DI container to own a scoped-context lifetime.
/// </summary>
public static class PaperbunkrDb
{
    public static PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={PaperbunkrDbContext.GetDefaultDatabasePath()}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    /// <summary>
    /// Applies pending migrations (creating the SQLite file on first run) and, if the library is
    /// empty, seeds it with demo data - the same series the wireframe's sample covers used, now
    /// as real persisted rows instead of hardcoded ViewModel data. Call once at startup.
    /// </summary>
    public static void EnsureCreatedAndSeeded()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        if (context.Series.Any())
        {
            return;
        }

        foreach (var seed in DemoSeries)
        {
            var series = new Series
            {
                Name = seed.Name,
                SortName = seed.Name,
                ContentType = seed.ContentType,
            };

            for (int i = 0; i < seed.UnreadCount; i++)
            {
                series.Issues.Add(new Issue { Number = (i + 1).ToString(), LastPageRead = null });
            }

            // A handful of already-read issues too, so series aren't just a bare unread count.
            int readIssues = seed.UnreadCount == 0 ? 3 : Math.Min(seed.UnreadCount, 3);
            for (int i = 0; i < readIssues; i++)
            {
                series.Issues.Add(new Issue { Number = $"R{i + 1}", LastPageRead = 1 });
            }

            if (seed.HasMissingIssue && series.Issues.Count > 0)
            {
                series.Issues[0].FileIsMissing = true;
            }

            context.Series.Add(series);
        }

        context.SaveChanges();
    }

    private static readonly (string Name, ContentType ContentType, int UnreadCount, bool HasMissingIssue)[] DemoSeries =
    {
        ("The Cartographer's Vault", ContentType.Comic, 5, false),
        ("Nightshift Orchid", ContentType.Manga, 2, false),
        ("Brass Horizon", ContentType.Comic, 0, true),
        ("Kilo Station", ContentType.Comic, 14, false),
        ("The Sovereign's Cage", ContentType.Manhwa, 9, false),
        ("Ironclad Requiem", ContentType.Comic, 0, false),
        ("Paper Moth", ContentType.Manga, 1, false),
        ("Ninth Hour Blade", ContentType.Manhua, 31, true),
        ("Ashlight", ContentType.Manhwa, 0, false),
        ("The Last Cartel", ContentType.Comic, 12, false),
        ("Iron Loom", ContentType.Manga, 0, false),
        ("Vanta Reach", ContentType.Manga, 3, false),
    };
}
