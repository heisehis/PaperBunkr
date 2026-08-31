using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Reads a comic archive's embedded <c>ComicInfo.xml</c> directly from the file, independent of
/// whatever any database (Paperbunkr's own, or an imported CE library) has cached for it.
/// Shared between <c>Paperbunkr.App.Services.LibraryFolderScanner</c> (a fresh folder scan) and
/// <see cref="CeMigration.CeLibraryMigrator"/> (docs/superpowers/specs/2026-08-31-ce-migration-
/// embedded-metadata-precedence-design.md) - both need the exact same "trust the file over any
/// cached row" read, extracted here rather than duplicated.
/// </summary>
public static class EmbeddedComicInfoReader
{
    /// <summary>Returns <see langword="null"/> for anything that doesn't pan out: a missing file,
    /// an unsupported/dynamic format, no embedded <c>ComicInfo.xml</c>, or a malformed one. Callers
    /// fall back to whatever their own secondary source is (filename parsing, a cached database
    /// row) in every case.</summary>
    public static ComicInfo? TryRead(string filePath)
    {
        try
        {
            using var provider = Providers.Readers.CreateSourceProvider(filePath);
            if (provider is not IInfoStorage infoStorage)
            {
                return null;
            }

            provider.Open(async: false);
            return infoStorage.LoadInfo(InfoLoadingMethod.Complete);
        }
        catch
        {
            return null;
        }
    }
}
