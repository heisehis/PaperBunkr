using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Library Health's "Publisher logos" section (docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md): which publishers
/// in the library have no logo, which issues have no publisher at all, a one-click fill from a series' other issues, and the shortcut to the user
/// icon folder. On-demand like Find Similar Series - nothing is scanned until the user asks.
/// </summary>
public sealed partial class PublisherGapsViewModel : ObservableObject
{
    private const int MaxRows = 300;

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly IDialogService? _dialogs;
    private readonly Func<string, bool> _hasLogo;
    private readonly Action<string> _openFolder;
    private readonly Action _reloadIcons;
    private readonly Func<int> _userIconCount;
    private readonly Func<string> _iconsFolder;

    public PublisherGapsViewModel(
        Func<PaperbunkrDbContext>? contextFactory = null,
        IDialogService? dialogs = null,
        Func<string, bool>? hasLogo = null,
        Action<string>? openFolder = null,
        Action? reloadIcons = null,
        Func<int>? userIconCount = null,
        Func<string>? iconsFolder = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _dialogs = dialogs;
        _hasLogo = hasLogo ?? (name => MarkResolver.Instance.ResolvePublisher(name).Kind is MarkKind.Raster or MarkKind.SvgAsset);
        _openFolder = openFolder ?? (path => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }));
        // Lambdas, not method groups: MarkResolver.Instance reads bundled assets, so it must not be touched until a command actually needs it.
        _reloadIcons = reloadIcons ?? (() => MarkResolver.Instance.ReloadUserIcons());
        _userIconCount = userIconCount ?? (() => MarkResolver.Instance.UserIconCount);
        _iconsFolder = iconsFolder ?? PublisherIconFolder.Ensure;
    }

    public ObservableCollection<PublisherWithoutLogo> WithoutLogo { get; } = new();

    public ObservableCollection<SeriesMissingPublisher> SeriesMissing { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(HeaderDetail))]
    private bool _isScanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFillable), nameof(FillLabel), nameof(HeaderDetail))]
    private int _fillableIssues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPublisher), nameof(HeaderDetail))]
    private int _issuesWithoutPublisher;

    [ObservableProperty]
    private int _publishersTotal;

    [ObservableProperty]
    private int _publishersWithLogo;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasResults => IsScanned && (WithoutLogo.Count > 0 || IssuesWithoutPublisher > 0);

    public bool HasWithoutLogo => WithoutLogo.Count > 0;

    public bool HasSeriesMissing => SeriesMissing.Count > 0;

    public bool HasNoPublisher => IssuesWithoutPublisher > 0;

    public bool HasFillable => FillableIssues > 0;

    public string FillLabel => $"Fill {FillableIssues:N0} from the rest of the series";

    /// <summary>The one-line summary beside the section's title.</summary>
    public string HeaderDetail => !IsScanned
        ? "· Not scanned yet"
        : $"· {WithoutLogo.Count:N0} without a logo · {IssuesWithoutPublisher:N0} issues with no publisher";

    public string UserIconsFolder => _iconsFolder();

    public string UserIconsSummary
    {
        get
        {
            int count = _userIconCount();
            return count == 0 ? "No icons added yet" : $"{count:N0} icon{(count == 1 ? string.Empty : "s")} in your folder";
        }
    }

    [RelayCommand]
    private void Scan()
    {
        using var context = _contextFactory();
        var report = PublisherCoverage.Scan(context, _hasLogo);

        WithoutLogo.Clear();
        foreach (var row in report.WithoutLogo.Take(MaxRows))
        {
            WithoutLogo.Add(row);
        }

        SeriesMissing.Clear();
        foreach (var row in report.SeriesMissing.Take(MaxRows))
        {
            SeriesMissing.Add(row);
        }

        PublishersTotal = report.PublishersTotal;
        PublishersWithLogo = report.PublishersWithLogo;
        IssuesWithoutPublisher = report.IssuesWithoutPublisher;
        FillableIssues = report.FillableIssues;
        IsScanned = true;
        StatusMessage = report.WithoutLogo.Count == 0 && report.IssuesWithoutPublisher == 0
            ? "Every publisher has a logo and every issue names one."
            : null;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasWithoutLogo));
        OnPropertyChanged(nameof(HasSeriesMissing));
        OnPropertyChanged(nameof(HeaderDetail));
        OnPropertyChanged(nameof(UserIconsSummary));
    }

    [RelayCommand]
    private async Task FillFromSiblings()
    {
        if (FillableIssues == 0)
        {
            return;
        }

        if (_dialogs is not null)
        {
            bool ok = await _dialogs.ConfirmAsync(
                $"Set the publisher on {FillableIssues:N0} issues to the one the rest of their series names? Issues that already have a publisher are not touched. " +
                "This changes the library only, not the files.",
                "Fill missing publishers", confirmLabel: "Fill");
            if (!ok)
            {
                return;
            }
        }

        int updated;
        using (var context = _contextFactory())
        {
            updated = PublisherCoverage.FillFromSiblings(context);
        }

        Scan();
        StatusMessage = $"Set the publisher on {updated:N0} issue{(updated == 1 ? string.Empty : "s")}.";
    }

    [RelayCommand]
    private void OpenIconsFolder() => _openFolder(_iconsFolder());

    /// <summary>Re-reads the user icon folder and rescans, so a logo you just added is reflected without restarting.</summary>
    [RelayCommand]
    private void ReloadIcons()
    {
        _reloadIcons();
        OnPropertyChanged(nameof(UserIconsSummary));
        if (IsScanned)
        {
            Scan();
        }
    }
}
