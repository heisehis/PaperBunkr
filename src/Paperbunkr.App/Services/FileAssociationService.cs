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
