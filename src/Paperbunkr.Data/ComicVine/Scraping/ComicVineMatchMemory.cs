namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>One remembered "this volume was chosen for a book with this search key" fact, backing <see cref="MatchScoreCalculator"/>'s priorscore term (CE's flat <c>SERIES_FILE</c> equivalent).</summary>
public class ComicVineMatchMemoryEntry
{
    public int Id { get; set; }

    public string SearchKey { get; set; } = string.Empty;

    public int ChosenVolumeId { get; set; }
}

/// <summary>Reads and writes <see cref="ComicVineMatchMemoryEntry"/> rows in the core database.</summary>
public sealed class ComicVineMatchMemory(Func<PaperbunkrDbContext> createContext)
{
    /// <summary>True if <paramref name="volumeId"/> was previously chosen for a book normalized to the same <paramref name="searchKey"/>.</summary>
    public bool WasChosen(string searchKey, int volumeId)
    {
        using var context = createContext();
        return context.ComicVineMatchMemories.Any(e => e.SearchKey == searchKey && e.ChosenVolumeId == volumeId);
    }

    /// <summary>Records the choice; idempotent.</summary>
    public void RecordChoice(string searchKey, int volumeId)
    {
        using var context = createContext();
        if (!context.ComicVineMatchMemories.Any(e => e.SearchKey == searchKey && e.ChosenVolumeId == volumeId))
        {
            context.ComicVineMatchMemories.Add(new ComicVineMatchMemoryEntry { SearchKey = searchKey, ChosenVolumeId = volumeId });
            context.SaveChanges();
        }
    }

    /// <summary>Normalizes a series name into a stable lookup key (case-insensitive, whitespace-collapsed) so trivial formatting differences don't miss a real match.</summary>
    public static string NormalizeSearchKey(string seriesName) =>
        string.Join(' ', seriesName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
