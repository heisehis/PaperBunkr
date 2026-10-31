using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

// Jump-back chip and the end-of-session toast (docs/superpowers/specs/2026-10-27-books-slice3a-reader-
// jumpback-session-design.md). One stored position, no history stack; the comic reader's chip is the model.
public partial class BookReaderScreenViewModel
{
    private static readonly TimeSpan JumpBackLifetime = TimeSpan.FromSeconds(6);

    /// <summary>A jump lands, then the page reports the position it landed on; that report must not dismiss the chip.</summary>
    internal static readonly TimeSpan JumpBackSettle = TimeSpan.FromMilliseconds(1500);

    [ObservableProperty]
    private bool _hasJumpBack;

    [ObservableProperty]
    private string? _jumpBackLabel;

    private BookPosition _jumpBackPosition;
    private DateTime _jumpBackSetAt;
    private DispatcherTimer? _jumpBackTimer;

    /// <summary>Test seam for the settle window.</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Raised with the toast text when a session of five minutes or more ends; null in tests.</summary>
    public Action<ToastRequest>? ShowToast { get; set; }

    public string JumpBackHint =>
        InputServiceLocator.Current.ShortcutText(InputActionIds.BookJumpBack) is { } text ? $"({text})" : string.Empty;

    /// <summary>Offers a way back when <paramref name="from"/> to <paramref name="to"/> is a large jump. Call before <c>_position</c> changes.</summary>
    private void NoteJump(BookPosition from, BookPosition to)
    {
        StopSpeaking();
        if (_source is null || !BookJumpTracker.IsLargeJump(from, to))
        {
            return;
        }

        _jumpBackPosition = from;
        _jumpBackSetAt = Clock();
        JumpBackLabel = from.ChapterIndex >= 0 && from.ChapterIndex < _source.Chapters.Count && from.ChapterIndex != to.ChapterIndex
            ? $"Back to Ch. {from.ChapterIndex + 1}"
            : "Back to where you were";
        HasJumpBack = true;
        OnPropertyChanged(nameof(JumpBackHint));

        if (_jumpBackTimer is null)
        {
            _jumpBackTimer = new DispatcherTimer { Interval = JumpBackLifetime };
            _jumpBackTimer.Tick += OnJumpBackExpired;
        }

        _jumpBackTimer.Stop();
        _jumpBackTimer.Start();
    }

    private void ClearJumpBack()
    {
        _jumpBackTimer?.Stop();
        HasJumpBack = false;
        JumpBackLabel = null;
    }

    /// <summary>A position report after the settle window means the reader moved on their own.</summary>
    private void ClearJumpBackAfterSettle()
    {
        if (HasJumpBack && Clock() - _jumpBackSetAt >= JumpBackSettle)
        {
            ClearJumpBack();
        }
    }

    internal void OnJumpBackExpired(object? sender, EventArgs e) => ClearJumpBack();

    [RelayCommand]
    private void JumpBack()
    {
        if (!HasJumpBack || _source is null)
        {
            return;
        }

        var target = _jumpBackPosition;
        StopSpeaking();
        ClearJumpBack();
        _history.Push(_position);
        _position = target;
        RecomputeCurrentPage();
        PersistPosition();
        PositionRestoreRequested?.Invoke(target.BlockId);
    }

    /// <summary>The Book reader's jump-back action: handles only while the chip shows.</summary>
    public bool TryJumpBack()
    {
        if (!HasJumpBack)
        {
            return false;
        }

        JumpBack();
        return true;
    }

    private void ShowSessionSummary(int activeSeconds, int pages)
    {
        if (ShowToast is null || activeSeconds < BookSessionSummary.MinimumActiveSeconds)
        {
            return;
        }

        string? goalLine = null;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            goalLine = BookSessionSummary.GoalLine(GoalResolver.Build(context, DateTime.UtcNow));
        }
        catch
        {
            // A goal lookup failure only costs the goal fragment.
        }

        if (BookSessionSummary.Format(activeSeconds, pages, goalLine) is { } text)
        {
            ShowToast(new ToastRequest("Reading session", text));
        }
    }
}
