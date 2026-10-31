using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.ViewModels;

// Reading aids: the line ruler and auto-scroll (docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md §4).
// The ruler's on/off state and the auto-scroll speed are remembered globally; auto-scroll itself always starts off. The drawing and the
// requestAnimationFrame loop live in the page (BookReaderScreen.axaml.cs), which follows these properties.
public partial class BookReaderScreenViewModel
{
    private bool _seedingAids;

    [ObservableProperty]
    private bool _isRulerOn;

    [ObservableProperty]
    private bool _isAutoScrolling;

    [ObservableProperty]
    private int _autoScrollSpeed = BookAutoScrollMath.DefaultSpeed;

    /// <summary>Raised with -1 / +1 when the ruler should move a line; the page applies it.</summary>
    public event Action<int>? RulerMoveRequested;

    /// <summary>Pixels the page scrolls each second at the current speed and line height.</summary>
    public double AutoScrollPixelsPerSecond => BookAutoScrollMath.PixelsPerSecond(AutoScrollSpeed, Settings.LineHeightPixels);

    /// <summary>The font sheet's switch binds straight to <see cref="IsAutoScrolling"/>, so paged mode refuses here too, not only in the command.</summary>
    partial void OnIsAutoScrollingChanged(bool value)
    {
        if (value && IsPaged)
        {
            IsAutoScrolling = false;
        }
    }

    partial void OnIsRulerOnChanged(bool value)
    {
        if (!_seedingAids)
        {
            SaveAidSetting(a => a.BookReaderRulerEnabled = value);
        }
    }

    partial void OnAutoScrollSpeedChanged(int value)
    {
        int clamped = BookAutoScrollMath.ClampSpeed(value);
        if (clamped != value)
        {
            AutoScrollSpeed = clamped;
            return;
        }

        if (!_seedingAids)
        {
            SaveAidSetting(a => a.BookReaderAutoScrollSpeed = clamped);
        }
    }

    private void SaveAidSetting(Action<Paperbunkr.Data.Entities.AppSettings> apply)
    {
        using var context = PaperbunkrDb.CreateContext();
        apply(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    private void SeedAids(Paperbunkr.Data.Entities.AppSettings appSettings)
    {
        _seedingAids = true;
        IsRulerOn = appSettings.BookReaderRulerEnabled;
        AutoScrollSpeed = BookAutoScrollMath.ClampSpeed(appSettings.BookReaderAutoScrollSpeed);
        IsAutoScrolling = false;
        _seedingAids = false;
    }

    [RelayCommand]
    private void ToggleRuler() => IsRulerOn = !IsRulerOn;

    [RelayCommand]
    private void RulerLineUp()
    {
        if (IsRulerOn)
        {
            RulerMoveRequested?.Invoke(-1);
        }
    }

    [RelayCommand]
    private void RulerLineDown()
    {
        if (IsRulerOn)
        {
            RulerMoveRequested?.Invoke(1);
        }
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        if (!IsAutoScrolling && IsPaged)
        {
            return; // a scroll-mode aid: paged mode turns pages instead (paged/scroll spec §5.5)
        }

        IsAutoScrolling = !IsAutoScrolling;
        _activity.NoteInput();
    }

    [RelayCommand]
    private void AutoScrollFaster()
    {
        AutoScrollSpeed++;
        _activity.NoteInput();
    }

    [RelayCommand]
    private void AutoScrollSlower()
    {
        AutoScrollSpeed--;
        _activity.NoteInput();
    }

    /// <summary>The page stopped scrolling on its own (end of the chapter, or the reader touched the page).</summary>
    public void OnAutoScrollStoppedByPage() => IsAutoScrolling = false;
}
