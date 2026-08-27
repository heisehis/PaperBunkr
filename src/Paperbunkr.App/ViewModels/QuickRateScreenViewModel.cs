using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Quick Rating + free-text Review in one popup (docs/ce-feature-inventory.md §A) - a lightweight
/// overlay reached from a right-click ("Quick Rate...") rather than opening the full single-book
/// Issue Properties editor just to set a rating/review. No CE precedent exists for this exact popup
/// shape (verified against <c>ComicBookDialog.cs</c>); <c>Issue.Rating</c>/<c>Issue.Review</c>
/// themselves are the same two fields the full editor already writes, deliberately not a separate
/// schema concept - editing one here and reopening the full editor (or vice versa) sees the same
/// value.
/// </summary>
public partial class QuickRateScreenViewModel : ViewModelBase
{
    private readonly System.Action _close;
    private readonly System.Func<PaperbunkrDbContext> _contextFactory;
    private int? _issueId;

    public QuickRateScreenViewModel(System.Action close) : this(close, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal QuickRateScreenViewModel(System.Action close, System.Func<PaperbunkrDbContext> contextFactory)
    {
        _close = close;
        _contextFactory = contextFactory;
    }

    [ObservableProperty] private string _headerLabel = string.Empty;
    [ObservableProperty] private int? _rating;
    [ObservableProperty] private string _review = string.Empty;

    public bool Star1 => (Rating ?? 0) >= 1;
    public bool Star2 => (Rating ?? 0) >= 2;
    public bool Star3 => (Rating ?? 0) >= 3;
    public bool Star4 => (Rating ?? 0) >= 4;
    public bool Star5 => (Rating ?? 0) >= 5;

    partial void OnRatingChanged(int? value)
    {
        OnPropertyChanged(nameof(Star1));
        OnPropertyChanged(nameof(Star2));
        OnPropertyChanged(nameof(Star3));
        OnPropertyChanged(nameof(Star4));
        OnPropertyChanged(nameof(Star5));
    }

    /// <summary>Toggle-to-clear: clicking the currently-set star unrates it - same convention as the full editor's star rows.</summary>
    private static int? ToggleStar(int? current, int star) => current == star ? null : star;

    [RelayCommand] private void SetStar1() => Rating = ToggleStar(Rating, 1);
    [RelayCommand] private void SetStar2() => Rating = ToggleStar(Rating, 2);
    [RelayCommand] private void SetStar3() => Rating = ToggleStar(Rating, 3);
    [RelayCommand] private void SetStar4() => Rating = ToggleStar(Rating, 4);
    [RelayCommand] private void SetStar5() => Rating = ToggleStar(Rating, 5);

    public void Load(int issueId)
    {
        _issueId = issueId;
        using var context = _contextFactory();
        var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        HeaderLabel = $"{issue.Series?.Name ?? "Unknown Series"} #{issue.EffectiveNumber()}";
        Rating = issue.Rating.HasValue ? (int)issue.Rating.Value : null;
        Review = issue.Review ?? string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (_issueId is not int issueId)
        {
            _close();
            return;
        }

        using var context = _contextFactory();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            issue.Rating = Rating.HasValue ? (float?)Rating.Value : null;
            issue.Review = string.IsNullOrWhiteSpace(Review) ? null : Review;
            context.SaveChanges();
        }

        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}
