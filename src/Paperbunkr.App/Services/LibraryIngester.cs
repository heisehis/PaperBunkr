using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Daemon.Import;

namespace Paperbunkr.App.Services;

/// <summary>
/// Puts an imported file into the library by running the app's own scanner on it (the daemon has no scanner - it is UI-free). If the file was
/// already picked up (the library folder's live watch can beat us to it) the existing issue is looked up by path instead, so the wanted issue
/// is still linked to it.
/// </summary>
public sealed class LibraryIngester : ILibraryIngester
{
    private readonly LibraryFolderScanner _scanner;

    public LibraryIngester(LibraryFolderScanner? scanner = null) => _scanner = scanner ?? new LibraryFolderScanner();

    public async Task<int?> IngestAsync(string filePath, CancellationToken cancellationToken)
    {
        var result = await _scanner.ImportNewFilesAsync(new[] { filePath }, new Progress<(int Done, int Total)>(), cancellationToken).ConfigureAwait(false);
        if (result.AddedIssueIds.Count > 0)
        {
            return result.AddedIssueIds[0];
        }

        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Where(i => i.FilePath == filePath).Select(i => (int?)i.Id).FirstOrDefault();
    }
}
