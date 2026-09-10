using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// File-association management (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md
/// §2) over the same registered format list <see cref="PageImageDecoder"/>/<see cref="LibraryFolderScanner"/>
/// already dispatch against (<c>Providers.Readers.GetSourceFormats()</c>) - no separate hardcoded
/// extension list. The registry itself is the source of truth for "is this associated" (matches
/// CE's own live-check pattern via <c>IsFileOpenRegistered</c>), so nothing is persisted in
/// <c>AppSettings</c> for this.
/// </summary>
public class FileAssociationService
{
    private const string TypeIdPrefix = "Paperbunkr.";

    /// <summary>
    /// The comic-specific extensions the installer's per-format association tasks (and the
    /// <c>--register-file-associations</c> / <c>--unregister-file-associations</c> CLI path in
    /// <see cref="Program"/>) are allowed to touch - see
    /// docs/superpowers/specs/2026-09-09-installer-redesign-design.md decision 5, plus the
    /// 2026-09-09 correction that restores <c>.cbt</c> for ComicRack CE parity.
    ///
    /// <para>
    /// Deliberately excludes the generic archive groups <see cref="GetAvailableFormats"/> also
    /// returns - <c>.zip</c> ("ZIP Archive"), <c>.rar</c> ("RAR Archive" / "RAR5 Archive"),
    /// <c>.7z</c> ("7z Archive"), <c>.tar</c> ("TAR Archive"): the install/CLI flow hijacking bare
    /// archive extensions as Paperbunkr files was a pre-existing over-association bug. Preferences
    /// &gt; Advanced is a separate post-install screen and keeps its own full
    /// <see cref="GetAvailableFormats"/> list untouched.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ComicAssociationExtensions = new[]
    {
        ".pdf", ".cbz", ".cbr", ".cb7", ".cbt", ".cbw", ".djvu",
    };

    private readonly IShellFileAssociation _shell;

    public FileAssociationService()
        : this(new WindowsShellFileAssociation())
    {
    }

    /// <summary>Test-only seam - production always uses the real registry-backed implementation.</summary>
    internal FileAssociationService(IShellFileAssociation shell)
    {
        _shell = shell;
    }

    public IReadOnlyList<FileAssociationSummary> GetAvailableFormats()
    {
        return Providers.Readers.GetSourceFormats()
            .GroupBy(f => f.Name)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var extensions = g.SelectMany(f => f.Extensions).Distinct().ToList();
                string typeId = TypeIdFor(g.Key);
                bool associated = extensions.Count > 0 && extensions.All(ext => _shell.IsRegistered(typeId, ext));
                return new FileAssociationSummary
                {
                    Name = g.Key,
                    ExtensionList = string.Join(", ", extensions),
                    IsAssociated = associated,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Registers (or unregisters) file associations for the requested comic extensions only,
    /// scoped by <see cref="ComicAssociationExtensions"/>. Drives the installer's per-format
    /// <c>[Tasks]</c> and the <see cref="Program"/> CLI path (decision 5 of the 2026-09-09
    /// installer redesign). Reuses the same per-format <see cref="SetAssociated"/> registry-write
    /// path as Preferences &gt; Advanced, so there is still exactly one owner of these keys.
    /// </summary>
    /// <param name="extensions">
    /// The extensions to act on (leading dot optional, case-insensitive). Anything not in
    /// <see cref="ComicAssociationExtensions"/> is silently ignored. Pass the whole allow-list for
    /// "all comic formats".
    /// </param>
    public void SetComicAssociationsFor(IEnumerable<string> extensions, bool associated)
    {
        var requested = extensions
            .Select(NormalizeExtension)
            .Where(ext => ComicAssociationExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requested.Count == 0)
        {
            return;
        }

        // Materialize before the loop: SetAssociated re-enters Providers.Readers.GetSourceFormats(),
        // and holding this enumerator open across that call would recursively acquire the provider
        // registry's read lock (which is not recursion-enabled). Same reason GetAvailableFormats
        // above ends in .ToList().
        var formatNames = Providers.Readers.GetSourceFormats()
            .Where(f => f.Extensions.Any(ext => requested.Contains(ext)))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (string formatName in formatNames)
        {
            SetAssociated(formatName, associated);
        }
    }

    private static string NormalizeExtension(string ext)
    {
        ext = ext.Trim();
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    public void SetAssociated(string formatName, bool associated)
    {
        var format = Providers.Readers.GetSourceFormats().FirstOrDefault(f => f.Name == formatName);
        if (format is null)
        {
            return;
        }

        string typeId = TypeIdFor(formatName);
        string appPath = Environment.ProcessPath ?? string.Empty;

        foreach (string ext in format.Extensions)
        {
            if (associated)
            {
                _shell.Register(typeId, ext, formatName, appPath);
            }
            else
            {
                _shell.Unregister(typeId, ext);
            }
        }

        _shell.RefreshShell();
    }

    private static string TypeIdFor(string formatName) => TypeIdPrefix + Regex.Replace(formatName, @"\W", string.Empty);
}
