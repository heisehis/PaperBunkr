using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class BookReaderScreen : UserControl
{
    private BookReaderScreenViewModel? _viewModel;

    public BookReaderScreen()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;

        // SizeChanged alone isn't reliable for the very first time this screen becomes visible:
        // if this control was already measured with its final size while IsVisible was still
        // false (its ContentControl's Content is bound eagerly from app startup, same as every
        // other screen), the size never actually *changes* when it's shown, so SizeChanged never
        // fires - leaving BookReaderScreenViewModel.RecomputeCurrentPage stuck behind its
        // viewport-not-yet-known guard and the reader blank. Loaded fires whenever this control is
        // attached and laid out, regardless of whether its size changed, so it closes that gap
        // without needing to know which of Avalonia's measure-while-hidden behaviors is in play.
        Loaded += OnLoaded;

        // The rail-nav screen switcher never destroys/recreates screens (MainWindow.axaml just
        // toggles a ContentControl's content) - same reasoning ReaderScreen.axaml.cs's own
        // DataContextChanged wiring documents. DataContext is set once, at startup, to a single
        // long-lived BookReaderScreenViewModel reused across every LoadBook call, so a one-time
        // PropertyChanged hook here is enough - it doesn't need to re-fire per book.
        DataContextChanged += OnDataContextChanged;
    }

    private bool _isPageReady;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _viewModel = DataContext as BookReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
            _viewModel.Highlights.CollectionChanged += OnHighlightsCollectionChanged;
            PushCurrentChapterHtml();
        }
    }

    private void OnHighlightsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        ApplyHighlightsToWebView();

    /// <summary>
    /// Parses a <see cref="HighlightScript"/>-sent message (docs/superpowers/specs/2026-09-02-books-
    /// reflow-reader-webview-redesign-design.md) and translates its WebView-local rect into
    /// <c>RootGrid</c>'s coordinate space via <c>ReaderWebView.TranslatePoint</c>.
    /// </summary>
    private async void OnReaderWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(e.Body))
        {
            return;
        }

        using var doc = JsonDocument.Parse(e.Body);
        var root = doc.RootElement;
        string? type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        if (type == "contentTap")
        {
            // Restores the reading pane's tap-to-toggle-chrome interaction (docs/superpowers/specs/
            // 2026-09-02-books-reflow-reader-webview-redesign-design.md) - see HighlightScript's own
            // doc comment on why this moved to a JS message instead of an Avalonia PointerPressed
            // handler.
            _viewModel.ToggleChromeCommand.Execute(null);
            return;
        }

        if (type == "announcePosition")
        {
            _viewModel.AnnounceReadingPositionCommand.Execute(null);
            return;
        }

        if (type == "pageTurn")
        {
            // Keyboard page-turn while focus is inside the WebView - see HighlightScript's own
            // keydown listener doc comment for why this needs a JS-forwarded message the same way
            // contentTap/announcePosition already do.
            string? direction = root.TryGetProperty("direction", out var directionProp) ? directionProp.GetString() : null;
            if (direction == "next")
            {
                await NextPageAsync();
            }
            else if (direction == "previous")
            {
                await PreviousPageAsync();
            }

            return;
        }

        var rectInWebView = new Rect(
            root.GetProperty("rectX").GetDouble(), root.GetProperty("rectY").GetDouble(),
            root.GetProperty("rectWidth").GetDouble(), root.GetProperty("rectHeight").GetDouble());
        var topLeft = ReaderWebView.TranslatePoint(rectInWebView.TopLeft, RootGrid) ?? rectInWebView.TopLeft;
        var anchor = new Rect(topLeft, rectInWebView.Size);

        if (type == "selection")
        {
            _viewModel.OnWebViewSelectionCompleted(
                root.GetProperty("blockId").GetString() ?? string.Empty,
                root.GetProperty("startOffset").GetInt32(),
                root.GetProperty("length").GetInt32(),
                root.GetProperty("text").GetString() ?? string.Empty,
                anchor);
        }
        else if (type == "highlightTap")
        {
            _viewModel.OnWebViewHighlightTapped(root.GetProperty("highlightId").GetInt32(), anchor);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookReaderScreenViewModel.CurrentChapterHtml))
        {
            PushCurrentChapterHtml();
        }
    }

    /// <summary>
    /// Books reflow reader WebView redesign (docs/superpowers/specs/2026-09-02-books-reflow-reader-
    /// webview-redesign-design.md, Step 6) - any font/spacing/theme slider or preset change re-injects
    /// the CSS variable layer live, without a full chapter reload (which would flicker and reset
    /// scroll position). <c>BookReaderSettings</c> raises a change for every one of the properties
    /// <see cref="BuildTypographyCss"/> reads (FontSize, FontFamilyOption, LineSpacing, Theme,
    /// CharacterSpacing, WordSpacing, ParagraphSpacing, PageMargin), so reacting to all of them
    /// uniformly here (rather than filtering by name) is correct, not just simpler.
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) => PushTypographyCss();

    /// <summary>
    /// Books reflow reader WebView redesign (docs/superpowers/specs/2026-09-02-books-reflow-reader-
    /// webview-redesign-design.md, Step 5) - **vertical scroll, not CSS multi-column.** The design's
    /// original plan (CSS `column-width`, matching Thorium/Readium) was tried twice, two different
    /// ways - once relying on `vw` units, once having JS measure the real rendered box and set
    /// `column-width` as an exact pixel value - and *both* produced the identical symptom against the
    /// real running app (the next column's text visibly bleeding in at the right edge). Two different,
    /// individually sound fixes failing identically is a strong signal the actual defect is somewhere
    /// in how this specific WebView hosting mode handles multi-column layout + horizontal overflow
    /// clipping generally, not in either fix's own math - not diagnosable further without live
    /// devtools access this session doesn't have. Switched to `#pb-content` as a single vertically-
    /// scrollable block (`overflow-y: auto`) instead: no columns, no horizontal-overflow-clipping
    /// question to get wrong, and plain vertical scroll is about as basic and reliably-implemented a
    /// browser behavior as exists. "Page turn" becomes a one-viewport-height `scrollTop` jump instead
    /// of a `scrollLeft` one - see <see cref="NextPageScript"/>/<see cref="PreviousPageScript"/>.
    /// </summary>
    private void PushCurrentChapterHtml()
    {
        string? chapterHtml = _viewModel?.CurrentChapterHtml;
        if (string.IsNullOrEmpty(chapterHtml) || _viewModel is null)
        {
            return;
        }

        _isPageReady = false;

        // pb-base-style: pagination mechanics only, never changes. pb-user-style: the live typography/
        // theme layer (Step 6) - baked in here for the *initial* render of each chapter (no
        // NavigationCompleted race to wait out), then re-injected in place via InvokeScript by
        // PushTypographyCss whenever a setting changes without needing a full reload.
        string document = $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <style id="pb-base-style">
              html, body { margin: 0; padding: 0; height: 100vh; overflow: hidden; }
              #pb-content { height: 100vh; overflow-y: auto; overflow-x: hidden; box-sizing: border-box; }
              img { max-width: 100%; height: auto; }
            </style>
            <style id="pb-user-style">{{BuildTypographyCss(_viewModel.Settings)}}</style>
            <style id="pb-highlight-style">
              .pb-highlight { cursor: pointer; }
              .pb-color-Yellow { background: rgba(255, 213, 79, 0.55); }
              .pb-color-Green { background: rgba(129, 199, 132, 0.55); }
              .pb-color-Blue { background: rgba(100, 181, 246, 0.55); }
              .pb-color-Pink { background: rgba(240, 98, 146, 0.55); }
            </style>
            </head>
            <body><div id="pb-content">{{chapterHtml}}</div></body>
            <script>{{HighlightScript}}</script>
            </html>
            """;

        ReaderWebView.NavigateToString(document);
    }

    // Books reflow reader WebView redesign (docs/superpowers/specs/2026-09-02-books-reflow-reader-
    // webview-redesign-design.md, Step 7). Selection capture: on mouseup inside #pb-content, resolves
    // the selection to a single BlockIdInjector block + a character offset/length within that block's
    // own text content (via a TreeWalker over text nodes, not raw innerHTML indices, since HTML tags
    // don't count as characters) - single-block selections only, a real documented limitation, not
    // a silent mishandling of cross-block drags. pbApplyHighlights/pbClearHighlights re-render the
    // full highlight set from scratch (unwrap-then-rewrap) rather than incrementally patching - a
    // chapter's highlight count is small enough that this is simpler and more robust than tracking
    // incremental DOM diffs. Both selection and highlight-tap message the host via invokeCSharpAction
    // (Avalonia.Controls.WebView's own JS bridge global, not the raw WebView2 API).
    private const string HighlightScript = """
        function pbBlockOf(node) {
            while (node && node.nodeType !== 1) node = node.parentNode;
            while (node && !node.id) node = node.parentNode;
            return node;
        }
        function pbOffsetWithin(block, container, offset) {
            var range = document.createRange();
            range.selectNodeContents(block);
            range.setEnd(container, offset);
            return range.toString().length;
        }
        function pbClearHighlights() {
            document.querySelectorAll('.pb-highlight').forEach(function (span) {
                var parent = span.parentNode;
                while (span.firstChild) parent.insertBefore(span.firstChild, span);
                parent.removeChild(span);
                parent.normalize();
            });
        }
        function pbFindRange(blockId, startOffset, length) {
            var block = document.getElementById(blockId);
            if (!block) return null;
            var walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, null);
            var pos = 0, startNode = null, startNodeOffset = 0, endNode = null, endNodeOffset = 0, node;
            while (node = walker.nextNode()) {
                var len = node.textContent.length;
                if (startNode === null && pos + len >= startOffset) { startNode = node; startNodeOffset = startOffset - pos; }
                if (endNode === null && pos + len >= startOffset + length) { endNode = node; endNodeOffset = startOffset + length - pos; break; }
                pos += len;
            }
            if (!startNode || !endNode) return null;
            var range = document.createRange();
            range.setStart(startNode, startNodeOffset);
            range.setEnd(endNode, endNodeOffset);
            return range;
        }
        window.pbApplyHighlights = function (json) {
            pbClearHighlights();
            var list = JSON.parse(json);
            for (var i = 0; i < list.length; i++) {
                var h = list[i];
                var range = pbFindRange(h.blockId, h.startOffset, h.length);
                if (!range) continue;
                var span = document.createElement('span');
                span.className = 'pb-highlight pb-color-' + h.color;
                span.dataset.highlightId = h.id;
                try { range.surroundContents(span); } catch (e) { /* selection wasn't confined to one element - skip */ }
            }
        };
        document.getElementById('pb-content').addEventListener('mouseup', function () {
            var sel = window.getSelection();
            if (!sel || sel.isCollapsed || sel.rangeCount === 0) return;
            var range = sel.getRangeAt(0);
            var text = sel.toString();
            if (!text || text.trim().length === 0) return;
            var startBlock = pbBlockOf(range.startContainer);
            var endBlock = pbBlockOf(range.endContainer);
            if (!startBlock || !endBlock || startBlock !== endBlock) return;
            var startOffset = pbOffsetWithin(startBlock, range.startContainer, range.startOffset);
            var rect = range.getBoundingClientRect();
            invokeCSharpAction(JSON.stringify({
                type: 'selection', blockId: startBlock.id, startOffset: startOffset, length: text.length, text: text,
                rectX: rect.left, rectY: rect.top, rectWidth: rect.width, rectHeight: rect.height
            }));
        });
        // Ctrl+Shift+W (docs/superpowers/specs/2026-09-01-books-reader-screen-reader-accessibility-
        // design.md) relies on Avalonia's UserControl.KeyBindings, which - same underlying reason as
        // the tap-to-toggle-chrome fix above - doesn't reliably see key events while the native
        // WebView has keyboard focus. Captured here and forwarded instead, so the shortcut keeps
        // working once the user has clicked into the reading pane (which is most of the time).
        // Right/PageDown/Space and Left/PageUp page-turning (real gap found via manual testing
        // 2026-09-02 - the reader had no keyboard page-turning at all) needs the identical split:
        // BookReaderScreen.axaml.cs's OnRootKeyDown covers focus-on-chrome, this covers focus-in-
        // WebView (the common case once the user has clicked into the reading pane).
        document.addEventListener('keydown', function (e) {
            if (e.ctrlKey && e.shiftKey && (e.key === 'W' || e.key === 'w')) {
                e.preventDefault();
                invokeCSharpAction(JSON.stringify({ type: 'announcePosition' }));
                return;
            }
            if (e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return;
            if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') {
                e.preventDefault();
                invokeCSharpAction(JSON.stringify({ type: 'pageTurn', direction: 'next' }));
            } else if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
                e.preventDefault();
                invokeCSharpAction(JSON.stringify({ type: 'pageTurn', direction: 'previous' }));
            }
        });
        document.getElementById('pb-content').addEventListener('click', function (e) {
            var span = e.target.closest ? e.target.closest('.pb-highlight') : null;
            if (span) {
                var rect = span.getBoundingClientRect();
                invokeCSharpAction(JSON.stringify({
                    type: 'highlightTap', highlightId: parseInt(span.dataset.highlightId, 10),
                    rectX: rect.left, rectY: rect.top, rectWidth: rect.width, rectHeight: rect.height
                }));
                return;
            }
            // Plain tap on the content (not a highlight, not the tail end of a drag-selection) -
            // toggles the chrome bars. This is the fix for a real gap: the old tap-to-toggle wiring
            // was a PointerPressed handler on an Avalonia Border that no longer exists now that
            // NativeWebView owns the full-bleed reading pane - native embedded controls generally
            // don't bubble pointer input through Avalonia's own routed-event system the way a normal
            // control does, so that handler stopped firing. Without this, there was no way to reach
            // the chrome (close/TOC/search/settings) at all once it was hidden.
            var sel = window.getSelection();
            if (sel && !sel.isCollapsed) return;
            invokeCSharpAction(JSON.stringify({ type: 'contentTap' }));
        });
        """;

    /// <summary>Re-renders every highlight in the current chapter from scratch - called after the chapter's initial load and after any create/delete, per <see cref="HighlightScript"/>'s own doc comment on why "clear and reapply" beats incremental patching here.</summary>
    private void ApplyHighlightsToWebView()
    {
        if (_viewModel is null)
        {
            return;
        }

        var payload = _viewModel.GetCurrentChapterHighlights()
            .Select(h => new { id = h.Id, blockId = h.BlockId, startOffset = h.StartOffset, length = h.Length, color = h.Color.ToString() });
        string json = JsonSerializer.Serialize(payload);
        _ = ReaderWebView.InvokeScript($"if (window.pbApplyHighlights) window.pbApplyHighlights({JsonSerializer.Serialize(json)});");
    }

    /// <summary>
    /// The three-layer CSS injection's "after" layer (docs/superpowers/specs/2026-09-02-books-reflow-
    /// reader-webview-redesign-design.md, Step 6) - declares <c>--pb-*</c> custom properties AND
    /// forces them onto <c>#pb-content</c>/its paragraphs with <c>!important</c>. Bare variable
    /// declarations alone wouldn't override a real EPUB chapter's own hardcoded <c>color</c>/
    /// <c>font-family</c> - this is the design's explicitly-called-out nuance, not an oversight.
    /// </summary>
    private static string BuildTypographyCss(BookReaderSettings settings)
    {
        string fontFamily = settings.FontFamilyOption switch
        {
            BookFontFamilyOption.Sans => "'Segoe UI', Arial, sans-serif",
            BookFontFamilyOption.Mono => "Consolas, monospace",
            BookFontFamilyOption.OpenDyslexic => "OpenDyslexic, Georgia, Cambria, serif",
            _ => "Georgia, Cambria, serif",
        };

        string background = ToCssColor(settings.Background);
        string foreground = ToCssColor(settings.Foreground);

        return $$"""
            :root {
              --pb-font-family: {{fontFamily}};
              --pb-font-size: {{settings.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
              --pb-line-height: {{settings.LineHeightMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
              --pb-letter-spacing: {{settings.CharacterSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
              --pb-word-spacing: {{settings.WordSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
              --pb-paragraph-spacing: {{settings.ParagraphSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
              --pb-page-margin: {{settings.PageMargin.ToString(System.Globalization.CultureInfo.InvariantCulture)}}px;
              --pb-bg: {{background}};
              --pb-fg: {{foreground}};
            }
            html, body { background: var(--pb-bg) !important; }
            #pb-content {
              font-family: var(--pb-font-family) !important;
              font-size: var(--pb-font-size) !important;
              line-height: var(--pb-line-height) !important;
              letter-spacing: var(--pb-letter-spacing) !important;
              word-spacing: var(--pb-word-spacing) !important;
              color: var(--pb-fg) !important;
              padding: var(--pb-page-margin) !important;
            }
            #pb-content p, #pb-content h1, #pb-content h2, #pb-content h3 {
              color: var(--pb-fg) !important;
              margin: 0 0 var(--pb-paragraph-spacing) 0 !important;
            }
            """;
    }

    private static string ToCssColor(IBrush brush) =>
        brush is ISolidColorBrush solid
            ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
            : "#000000";

    private void PushTypographyCss()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        string css = BuildTypographyCss(_viewModel.Settings);
        string script = "var el = document.getElementById('pb-user-style'); if (el) el.textContent = " + JsonSerializer.Serialize(css) + ";";
        _ = ReaderWebView.InvokeScript(script);
    }

    // "moved:<fraction>" reports the new scrollTop/scrollHeight position after a successful
    // within-chapter scroll; "end"/"start" means the WebView is already at that chapter's boundary -
    // the caller falls back to the ViewModel's chapter-advance commands in that case. Vertical
    // scrollTop, not horizontal scrollLeft - see PushCurrentChapterHtml's doc comment for why this
    // isn't CSS-multi-column-based anymore.
    private const string NextPageScript = """
        (function() {
            var el = document.getElementById('pb-content');
            if (!el) return 'end';
            var step = el.clientHeight;
            var before = el.scrollTop;
            el.scrollTop += step;
            if (el.scrollTop <= before) return 'end';
            var maxScroll = el.scrollHeight - step;
            return 'moved:' + (maxScroll > 0 ? (el.scrollTop / maxScroll) : 0);
        })();
        """;

    private const string PreviousPageScript = """
        (function() {
            var el = document.getElementById('pb-content');
            if (!el) return 'start';
            var step = el.clientHeight;
            var before = el.scrollTop;
            el.scrollTop -= step;
            if (el.scrollTop >= before) return 'start';
            var maxScroll = el.scrollHeight - step;
            return 'moved:' + (maxScroll > 0 ? (el.scrollTop / maxScroll) : 0);
        })();
        """;

    // ReaderChrome.PreviousRequested/NextRequested (docs/superpowers/specs/2026-09-03-books-reader-
    // hud-redesign-design.md) - plain EventHandler, not RoutedEventArgs, since these fire from the
    // shared control's own Click handling rather than a Button.Click routed event on this screen.
    private async void OnNextButtonClick(object? sender, EventArgs e) => await NextPageAsync();

    private async void OnPreviousButtonClick(object? sender, EventArgs e) => await PreviousPageAsync();

    /// <summary>
    /// Shared with the Right-arrow/PageDown keyboard path (<see cref="OnRootKeyDown"/>/
    /// <see cref="HighlightScript"/>'s <c>pageTurn</c> message) so both routes to "next page" - mouse
    /// click and keyboard, chrome-focused or WebView-focused - go through identical logic instead of
    /// two copies that could quietly drift apart.
    /// </summary>
    private async System.Threading.Tasks.Task NextPageAsync()
    {
        string? result = DecodeScriptResult(await ReaderWebView.InvokeScript(NextPageScript));
        if (_viewModel is null)
        {
            return;
        }

        if (result is null || result == "end")
        {
            _viewModel.NextPageCommand.Execute(null);
            return;
        }

        ApplyScrollResult(result);
    }

    /// <summary>Shared with the Left-arrow/PageUp keyboard path - see <see cref="NextPageAsync"/>'s own doc comment for why this is a shared method rather than duplicated per input source.</summary>
    private async System.Threading.Tasks.Task PreviousPageAsync()
    {
        string? result = DecodeScriptResult(await ReaderWebView.InvokeScript(PreviousPageScript));
        if (_viewModel is null)
        {
            return;
        }

        if (result is null || result == "start")
        {
            _viewModel.PreviousPageCommand.Execute(null);
            return;
        }

        ApplyScrollResult(result);
    }

    /// <summary>
    /// <see cref="NativeWebView.InvokeScript"/> hands back WebView2's raw
    /// <c>ExecuteScriptAsync</c> result verbatim (confirmed via <c>ildasm</c> against the installed
    /// 12.1.0 assembly, not assumed: <c>WebView2BaseAdapter.InvokeScript</c>'s completion handler
    /// calls <c>TaskCompletionSource.TrySetResult</c> directly on the COM callback's
    /// <c>resultObjectAsJson</c> argument, with no decoding step anywhere in that path) - i.e. a
    /// *JSON-encoded* string, not the bare JS string <see cref="NextPageScript"/>/
    /// <see cref="PreviousPageScript"/> actually return. A JS string result of <c>"end"</c> arrives
    /// here as the 5-character literal <c>"end"</c> (with its quotes), so every direct comparison
    /// below this call used to silently never match - the chapter-boundary fallback
    /// (<c>NextPageCommand</c>/<c>PreviousPageCommand</c>) never fired, which is exactly what "the
    /// next page button doesn't work" looks like on a book whose first chapter is a short cover
    /// page with nothing to scroll (the very first click already hits "end").
    /// </summary>
    private static string? DecodeScriptResult(string? raw) =>
        raw is null ? null : JsonSerializer.Deserialize<string>(raw);

    /// <summary>
    /// Known Step 5 simplification: <c>CanGoPrevious</c> stays driven by the ViewModel's chapter
    /// history (unchanged from before) rather than also tracking "scrolled partway into this chapter,
    /// so Previous should be enabled even with empty history" - that needs the position/locator work
    /// in Step 8 to track cleanly. Documented gap, not silently dropped.
    /// </summary>
    private void ApplyScrollResult(string result)
    {
        if (_viewModel is null || !result.StartsWith("moved:", StringComparison.Ordinal))
        {
            return;
        }

        if (double.TryParse(result.AsSpan("moved:".Length), System.Globalization.CultureInfo.InvariantCulture, out double fraction))
        {
            _viewModel.ProgressPercent = Math.Clamp(fraction, 0, 1) * 100;
        }
    }

    /// <summary>Pixels from the top within which a pointer move is treated as "near the top edge" for chrome auto-hide reveal (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md) - roughly the height of the top chrome bar itself.</summary>
    private const double AutoHideRevealZonePixels = 60;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PushViewportSize();
        RootGrid.Focus();
    }

    private void OnReaderNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _isPageReady = true;
        PushTypographyCss();
        ApplyHighlightsToWebView();
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            bool nearTopEdge = e.GetPosition(RootGrid).Y < AutoHideRevealZonePixels;
            vm.NotifyPointerActivity(nearTopEdge);
        }
    }

    /// <summary>
    /// Real gap found via manual testing 2026-09-02: the reader had no keyboard page-turning at all
    /// (unlike the comic reader's fully configurable Left/Right key bindings) - only Ctrl+Shift+W was
    /// ever wired. Right/PageDown/Space advance, Left/PageUp go back, handled here only for when focus
    /// is on chrome/RootGrid rather than inside the WebView - see <see cref="HighlightScript"/>'s own
    /// <c>keydown</c> listener for the WebView-focused case (same split Ctrl+Shift+W already needed).
    /// </summary>
    private async void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BookReaderScreenViewModel vm)
        {
            return;
        }

        vm.NotifyKeyActivity();

        if (e.Key is Key.Right or Key.PageDown or Key.Space)
        {
            e.Handled = true;
            await NextPageAsync();
        }
        else if (e.Key is Key.Left or Key.PageUp)
        {
            e.Handled = true;
            await PreviousPageAsync();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => PushViewportSize();

    private void PushViewportSize()
    {
        if (DataContext is BookReaderScreenViewModel vm && Bounds.Width > 0 && Bounds.Height > 0)
        {
            vm.UpdateViewportSize(Bounds.Size);
        }
    }

    /// <summary>Tapping the dimmed backdrop behind any drawer/sheet/overlay closes whichever is open - the other close calls are harmless no-ops for the ones that aren't.</summary>
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            vm.CloseTocCommand.Execute(null);
            vm.CloseFontSheetCommand.Execute(null);
            vm.CloseBookmarksCommand.Execute(null);
            vm.CloseHighlightsCommand.Execute(null);
            vm.CloseSearchCommand.Execute(null);
        }

        e.Handled = true;
    }

}
