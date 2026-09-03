using System;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderChrome"/> (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-
/// design.md). Deliberately does NOT host the control in a real headless <see cref="Avalonia.Controls.Window"/>
/// to reach its visual tree - three different attempts to do so (bare Measure/Arrange, a shown Window,
/// a Window marshaled through <c>Dispatcher.UIThread.Invoke</c>) either found 0 descendants or threw
/// "The calling thread cannot access this object because a different thread owns it" once the whole
/// ~1500-test App.Tests suite ran together (every attempt passed running this class alone - this is a
/// full-suite-scale xUnit-thread-vs-Avalonia-Compositor-thread-affinity problem, confirmed empirically,
/// not assumed). Rather than force a flaky full-tree test, this covers what's reliably testable without
/// one: the button/command-property wiring itself, and the click-vs-command split via the click
/// handlers directly (made <c>internal</c> for exactly this - see their own doc comments). The button's
/// actual IsVisible-per-null-command XAML binding and on-screen appearance are verified manually
/// (Steps 4/8/9 of the implementation plan), the same "manual/on-screen only" category this codebase
/// already uses for anything a headless test can't meaningfully assert.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderChromeTests
{
    private sealed class StubCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    private sealed class RelayStubCommand : ICommand
    {
        private readonly Action _execute;
        public RelayStubCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }

    [Fact]
    public void CommandProperties_RoundTrip()
    {
        var command = new StubCommand();
        var chrome = new ReaderChrome
        {
            TocCommand = command,
            SearchCommand = command,
            BookmarksCommand = command,
            HighlightsCommand = command,
            ExportCommand = command,
            CapturesCommand = command,
            CaptureToggleCommand = command,
            FontThemeCommand = command,
            CloseCommand = command,
            PreviousCommand = command,
            NextCommand = command,
        };

        Assert.Same(command, chrome.TocCommand);
        Assert.Same(command, chrome.SearchCommand);
        Assert.Same(command, chrome.BookmarksCommand);
        Assert.Same(command, chrome.HighlightsCommand);
        Assert.Same(command, chrome.ExportCommand);
        Assert.Same(command, chrome.CapturesCommand);
        Assert.Same(command, chrome.CaptureToggleCommand);
        Assert.Same(command, chrome.FontThemeCommand);
        Assert.Same(command, chrome.CloseCommand);
        Assert.Same(command, chrome.PreviousCommand);
        Assert.Same(command, chrome.NextCommand);
    }

    [Fact]
    public void EveryFeatureCommand_IsNullByDefault()
    {
        // PDF's real shape: it only ever binds 4 of these 9 (Captures/CaptureToggle/FontTheme/Close),
        // leaving the rest exactly like a fresh ReaderChrome - the XAML's IsVisible-when-non-null
        // binding is what turns this into "PDF simply doesn't show a TOC/Search/Bookmarks/Highlights/
        // Export button", verified on-screen rather than here (see this class's own doc comment).
        var chrome = new ReaderChrome();

        Assert.Null(chrome.TocCommand);
        Assert.Null(chrome.SearchCommand);
        Assert.Null(chrome.BookmarksCommand);
        Assert.Null(chrome.HighlightsCommand);
        Assert.Null(chrome.ExportCommand);
        Assert.Null(chrome.CapturesCommand);
        Assert.Null(chrome.CaptureToggleCommand);
        Assert.Null(chrome.FontThemeCommand);
        Assert.Null(chrome.CloseCommand);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsNotNullConverter_MatchesReaderChromeAxamlsOwnUsage(bool bindCommand)
    {
        // ReaderChrome.axaml binds every feature button's IsVisible via ObjectConverters.IsNotNull -
        // this pins down that this exact converter (Avalonia framework code, not this project's own)
        // does what the XAML wiring assumes for both a bound and an unbound command.
        ICommand? command = bindCommand ? new StubCommand() : null;
        object? result = ObjectConverters.IsNotNull.Convert(command, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.Equal(bindCommand, result);
    }

    [Fact]
    public void ProgressProperties_RoundTrip()
    {
        var chrome = new ReaderChrome { ProgressFraction = 0.42, ProgressLabel = "Chapter 6 · 42%" };

        Assert.Equal(0.42, chrome.ProgressFraction);
        Assert.Equal("Chapter 6 · 42%", chrome.ProgressLabel);
    }

    [Fact]
    public void OnPreviousButtonClick_RaisesPreviousRequested_EvenWithNoCommandBound()
    {
        var chrome = new ReaderChrome();
        bool fired = false;
        chrome.PreviousRequested += (_, _) => fired = true;

        chrome.OnPreviousButtonClick(null, new RoutedEventArgs());

        Assert.True(fired);
    }

    [Fact]
    public void OnNextButtonClick_RaisesNextRequested_AndExecutesTheBoundCommand()
    {
        var chrome = new ReaderChrome();
        bool eventFired = false;
        bool commandExecuted = false;
        chrome.NextRequested += (_, _) => eventFired = true;
        chrome.NextCommand = new RelayStubCommand(() => commandExecuted = true);

        chrome.OnNextButtonClick(null, new RoutedEventArgs());

        Assert.True(eventFired);
        Assert.True(commandExecuted);
    }

    [Fact]
    public void OnPreviousButtonClick_DoesNotThrow_WhenNoCommandBound()
    {
        var chrome = new ReaderChrome();
        chrome.OnPreviousButtonClick(null, new RoutedEventArgs());
    }
}
