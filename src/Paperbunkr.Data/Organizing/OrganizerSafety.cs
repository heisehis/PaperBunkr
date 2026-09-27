using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Organizing;

/// <summary>
/// Checks a profile can be run without turning your library against itself. A profile in <see cref="OrganizerMode.Copy"/> mode leaves every
/// original where it is and writes a second file; if that second file lands inside a folder the library WATCHES, the watcher imports it as a new
/// comic - one duplicate for every file copied.
/// </summary>
public static class OrganizerSafety
{
    /// <summary>A Copy profile whose base folder is inside (or is) a watched library folder, with the folder it is inside.</summary>
    public sealed record WatchedFolderCopy(OrganizerProfile Profile, string WatchedFolder);

    public static IReadOnlyList<WatchedFolderCopy> CopiesIntoWatchedFolders(IEnumerable<OrganizerProfile> profiles, IEnumerable<string> watchedFolders)
    {
        var watched = watchedFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Normalize).ToList();
        var result = new List<WatchedFolderCopy>();
        foreach (OrganizerProfile profile in profiles.Where(p => p.Mode == OrganizerMode.Copy && !string.IsNullOrWhiteSpace(p.BaseFolder)))
        {
            string baseFolder = Normalize(profile.BaseFolder);
            string? hit = watched.FirstOrDefault(w =>
                string.Equals(baseFolder, w, StringComparison.OrdinalIgnoreCase)
                || baseFolder.StartsWith(w + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                result.Add(new WatchedFolderCopy(profile, hit));
            }
        }

        return result;
    }

    /// <summary>The plain-language reason a run is refused, naming each offending profile.</summary>
    public static string Describe(IReadOnlyList<WatchedFolderCopy> problems) =>
        string.Join(" ", problems.Select(p =>
            $"The profile \"{p.Profile.Name}\" copies files into {p.Profile.BaseFolder}, which is inside the watched library folder {p.WatchedFolder}. " +
            "The library would import every copy as a duplicate comic.")) +
        " Choose a base folder outside your watched folders, or switch the profile to Move. Nothing was copied.";

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
