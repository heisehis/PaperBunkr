using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IOpenBooksManager"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4) - <c>inNewSlot</c> is dropped from CE's original signature since Paperbunkr's
/// <see cref="MainViewModel"/> is single-screen, not MDI.
/// </summary>
public sealed class PaperbunkrOpenBooksManager : IOpenBooksManager
{
    private readonly MainViewModel _main;

    public PaperbunkrOpenBooksManager(MainViewModel main) => _main = main;

    public bool Open(Issue issue, int page)
    {
        _main.OpenReaderForPlugin(issue.Id);
        if (page > 0)
        {
            _main.Reader.GoToPage(page);
        }

        return true;
    }

    public bool OpenFile(string file, int page)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.FirstOrDefault(i => i.FilePath == file);
        return issue is not null && Open(issue, page);
    }

    public bool IsOpen(Issue issue) => _main.IsIssueOpenInReaderForPlugin(issue.Id);
}
