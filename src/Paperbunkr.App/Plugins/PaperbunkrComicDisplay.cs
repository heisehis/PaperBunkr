using System;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IComicDisplay"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4) - deliberately scoped to what <see cref="ReaderScreenViewModel"/> actually exposes, not a
/// full port of CE's ~30-member GDI+ interface.
/// </summary>
public sealed class PaperbunkrComicDisplay : IComicDisplay
{
    private readonly ReaderScreenViewModel _reader;

    public PaperbunkrComicDisplay(ReaderScreenViewModel reader)
    {
        _reader = reader;
        _reader.CurrentPageIndexChanged += OnCurrentPageIndexChanged;
    }

    public Issue? CurrentBook => _reader.LoadedIssue;

    public int CurrentPageIndex => _reader.CurrentPageIndex;

    public int PageCount => _reader.PageCount;

    public event Action<int>? CurrentPageIndexChanged;

    private void OnCurrentPageIndexChanged(int index) => CurrentPageIndexChanged?.Invoke(index);

    public void NextPage() => _reader.GoToPage(_reader.CurrentPageIndex + 1);

    public void PreviousPage() => _reader.GoToPage(_reader.CurrentPageIndex - 1);

    public void GoToPage(int index) => _reader.GoToPage(index);
}
