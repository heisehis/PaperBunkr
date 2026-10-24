using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.ViewModels;

/// <summary>A provider this series is linked to, with a Refresh button.</summary>
public sealed partial class MangaSourceLinkRowViewModel : ViewModelBase
{
    private readonly MangaSeriesSourcesViewModel _owner;

    public MangaSourceLinkRowViewModel(MangaProviderLink link, MangaSeriesSourcesViewModel owner)
    {
        Link = link;
        _owner = owner;
    }

    public MangaProviderLink Link { get; }

    public string ProviderLabel => Link.Provider.ToString();

    public string ExternalId => Link.ExternalId;

    public string? Url => Link.Url;

    public string LastFetchedText => Link.LastFetchedAt is DateTime when ? $"Fetched {when.ToLocalTime():MMM d, yyyy}" : "Not fetched yet";

    [RelayCommand]
    private Task Refresh() => _owner.RefreshAsync(this);
}

/// <summary>One value a provider offered, with what the series holds now, and Accept / Reject.</summary>
public sealed partial class MangaSourceCandidateRowViewModel : ViewModelBase
{
    private readonly MangaSeriesSourcesViewModel _owner;

    public MangaSourceCandidateRowViewModel(MangaCandidate candidate, MangaSeriesSourcesViewModel owner)
    {
        Candidate = candidate;
        _owner = owner;
    }

    public MangaCandidate Candidate { get; }

    public string FieldLabel => Candidate.FieldLabel;

    public string CurrentText => Candidate.Current.Length == 0 ? "(nothing)" : Candidate.Current;

    public string ProposedText => Candidate.Proposed;

    public string ProviderText => Candidate.Provider is { } p ? $"via {p}" : string.Empty;

    /// <summary>A hand-set value: the provider could not change it, and accepting replaces what you wrote.</summary>
    public bool IsLocked => Candidate.IsLocked;

    public bool NeedsRole => Candidate.NeedsRole;

    public string[] RoleOptions { get; } = MangaInheritedFields.CreditRoles.ToArray();

    /// <summary>For a joined author/artist list: which credit the names go under.</summary>
    [ObservableProperty]
    private string _role = "Writer";

    [RelayCommand]
    private void Accept() => _owner.Accept(this);

    [RelayCommand]
    private void Reject() => _owner.Reject(this);
}

/// <summary>
/// The Sources tab of Edit Series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §11): the providers this series is linked to, a Refresh for each, and the
/// candidates a refresh left for the user to accept or reject, shown against what the series holds. Accepting or refreshing changes the series, so both wait until the editor
/// has no unsaved edits (otherwise the next Save would quietly undo them), then the editor reloads. The rows are rebuilt on the next dispatcher tick, never from inside their own button's click.
/// </summary>
public sealed partial class MangaSeriesSourcesViewModel : ViewModelBase
{
    private readonly MangaSourceCandidates _candidates;
    private readonly Func<ExternalMetadataProvider, IMetadataProvider?> _providers;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<string, string>? _notify;
    private readonly Action<int>? _enqueueWriteBack;
    private readonly Func<bool> _hasUnsavedEdits;
    private readonly Action _reloadEditor;
    private int _seriesId;

    public MangaSeriesSourcesViewModel(
        MangaSourceCandidates candidates,
        Func<ExternalMetadataProvider, IMetadataProvider?> providers,
        Func<PaperbunkrDbContext> contextFactory,
        Action<string, string>? notify,
        Action<int>? enqueueWriteBack,
        Func<bool> hasUnsavedEdits,
        Action reloadEditor)
    {
        _candidates = candidates;
        _providers = providers;
        _contextFactory = contextFactory;
        _notify = notify;
        _enqueueWriteBack = enqueueWriteBack;
        _hasUnsavedEdits = hasUnsavedEdits;
        _reloadEditor = reloadEditor;
    }

    public ObservableCollection<MangaSourceLinkRowViewModel> Links { get; } = new();

    public ObservableCollection<MangaSourceCandidateRowViewModel> Candidates { get; } = new();

    public bool HasLinks => Links.Count > 0;

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>Also import the tags the provider marks as spoilers the next time you refresh (off by default).</summary>
    [ObservableProperty]
    private bool _includeSpoilerTags;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Accepts every waiting candidate in one go, except those for a field you set by hand and the reading-mode suggestion (those need a decision of their own). Then the editor reloads.
    /// </summary>
    [RelayCommand]
    private void AcceptAll()
    {
        if (Blocked())
        {
            return;
        }

        var rows = Candidates.Where(c => !c.IsLocked && c.Candidate.Field != MetadataProposalField.ReadingMode).ToList();
        int applied = 0;
        foreach (var row in rows)
        {
            var result = _candidates.Accept(row.Candidate.ProposalId, row.NeedsRole ? row.Role : null);
            applied++;
            if (_enqueueWriteBack is not null)
            {
                foreach (int issueId in result.ChangedIssueIds)
                {
                    _enqueueWriteBack(issueId);
                }
            }
        }

        StatusText = applied == 0 ? "Nothing to accept." : $"Accepted {applied} offered value{(applied == 1 ? "" : "s")}. Hand-set fields and the reading mode were left for you to decide.";
        Dispatcher.UIThread.Post(_reloadEditor);
    }

    [ObservableProperty]
    private bool _isBusy;

    public void Load(int seriesId)
    {
        _seriesId = seriesId;
        Links.Clear();
        foreach (var link in _candidates.Links(seriesId))
        {
            Links.Add(new MangaSourceLinkRowViewModel(link, this));
        }

        Candidates.Clear();
        foreach (var candidate in _candidates.List(seriesId))
        {
            Candidates.Add(new MangaSourceCandidateRowViewModel(candidate, this));
        }

        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(HasCandidates));
    }

    private bool Blocked()
    {
        if (!_hasUnsavedEdits())
        {
            return false;
        }

        _notify?.Invoke("Save your edits first", "Save or discard what you have changed, then use Sources, so it isn't overwritten.");
        return true;
    }

    /// <summary>Fetches the provider again through the existing link path. A field you set by hand is not touched (it comes back as a candidate), and the rest updates as it always did.</summary>
    public async Task RefreshAsync(MangaSourceLinkRowViewModel row)
    {
        if (IsBusy || Blocked())
        {
            return;
        }

        var provider = _providers(row.Link.Provider);
        if (provider is null)
        {
            StatusText = $"{row.ProviderLabel} can't be refreshed from here.";
            return;
        }

        bool includeSpoilers = IncludeSpoilerTags;
        IsBusy = true;
        StatusText = $"Fetching from {row.ProviderLabel}…";
        try
        {
            MangaApplyResult? report = null;
            bool linked = await Task.Run(async () =>
            {
                using var context = _contextFactory();
                return await MetadataLinkResolver.LinkAsync(provider, context, _seriesId, row.ExternalId, CancellationToken.None, new MangaApplyOptions(includeSpoilers), r => report = r);
            });

            // Says what it did (applied / offered / kept), so a refresh is never a silent change.
            StatusText = linked ? $"Refreshed from {row.ProviderLabel}. {report?.Describe() ?? string.Empty}".TrimEnd() : $"Couldn't fetch from {row.ProviderLabel}. Try again later.";
            if (linked)
            {
                EnqueueDirty();
                Dispatcher.UIThread.Post(_reloadEditor);
            }
        }
        catch (MetadataProviderUnavailableException)
        {
            StatusText = $"{row.ProviderLabel} isn't reachable right now.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't refresh: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Accept(MangaSourceCandidateRowViewModel row)
    {
        if (Blocked())
        {
            return;
        }

        var result = _candidates.Accept(row.Candidate.ProposalId, row.NeedsRole ? row.Role : null);
        if (_enqueueWriteBack is not null)
        {
            foreach (int issueId in result.ChangedIssueIds)
            {
                _enqueueWriteBack(issueId);
            }
        }

        StatusText = $"Applied {row.FieldLabel.ToLowerInvariant()} from the provider.";
        Dispatcher.UIThread.Post(_reloadEditor);
    }

    public void Reject(MangaSourceCandidateRowViewModel row)
    {
        _candidates.Reject(row.Candidate.ProposalId);
        StatusText = $"Dismissed the offered {row.FieldLabel.ToLowerInvariant()}.";
        Dispatcher.UIThread.Post(() => Load(_seriesId));
    }

    private void EnqueueDirty()
    {
        if (_enqueueWriteBack is null)
        {
            return;
        }

        using var context = _contextFactory();
        foreach (int issueId in context.Issues.Where(i => i.SeriesId == _seriesId && i.EmbeddedDirty).Select(i => i.Id).ToList())
        {
            _enqueueWriteBack(issueId);
        }
    }
}
