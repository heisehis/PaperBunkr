using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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

    /// <summary>A block to scroll/page to once the reading pane is next ready - see <see cref="OnPositionRestoreRequested"/>'s own doc comment.</summary>
    private string? _pendingScrollBlockId;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
            _viewModel.PositionRestoreRequested -= OnPositionRestoreRequested;
        }

        _viewModel = DataContext as BookReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
            _viewModel.Highlights.CollectionChanged += OnHighlightsCollectionChanged;
            _viewModel.PositionRestoreRequested += OnPositionRestoreRequested;
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

        if (type == "position")
        {
            // Topmost-visible-block report (docs/superpowers/specs/2026-09-07-books-reader-
            // pagination-and-position-fix-design.md) - fired on every page-turn and from a debounced
            // scroll listener in the vertical-scroll fallback mode (see HighlightScript's own
            // pbSchedulePositionCapture/pbReportPosition).
            _viewModel.OnPositionCaptured(
                root.GetProperty("blockId").GetString() ?? string.Empty,
                root.GetProperty("fraction").GetDouble(),
                root.GetProperty("excerpt").GetString() ?? string.Empty);
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
    /// Third pagination attempt (docs/superpowers/specs/2026-09-07-books-reader-pagination-and-
    /// position-fix-design.md), replacing the 2026-09-02 vertical-scroll fallback. The original design
    /// (CSS `column-width`, `scrollLeft` page-turns) was tried twice and both attempts produced the
    /// identical symptom - the next column's text visibly bleeding in at the right edge - regardless of
    /// how the column width was computed, which points at the *scroll* mechanism itself (a known class
    /// of embedded-control repaint bug when native-scrolling multi-column content) rather than either
    /// fix's math. This attempt keeps CSS `column-width` for layout but drives page-turns via
    /// `transform: translateX()` on <c>#pb-content</c> instead - no native scroll happens at all, so
    /// that repaint path can't be hit. <see cref="UseColumnPaging"/> is a one-line flip back to the
    /// vertical-scroll fallback (now dressed with CSS scroll-snap) if this doesn't hold up on-screen -
    /// see that field's own doc comment.
    /// </summary>
    private void PushCurrentChapterHtml()
    {
        string? chapterHtml = _viewModel?.CurrentChapterHtml;
        if (string.IsNullOrEmpty(chapterHtml) || _viewModel is null)
        {
            return;
        }

        _isPageReady = false;

        // #pb-viewport is always the fixed-size clipping/scrolling frame; #pb-content is always the
        // actual flowing chapter content - unified across both modes so HighlightScript's JS doesn't
        // need two different DOM shapes. Column geometry (column-width/height) is set by JS at
        // runtime (HighlightScript's pbApplyPagination), not here, since it needs the real rendered
        // viewport size, not a guessed CSS unit - the same "vw vs exact pixel" question that didn't
        // matter for the previous scrollLeft attempts is sidestepped by measuring directly either way.
        string modeCss = UseColumnPaging
            ? """
              #pb-content { column-gap: 0; will-change: transform; }
              """
            : """
              #pb-viewport { overflow-y: auto; }
              #pb-content { scroll-snap-type: y proximity; }
              #pb-content > * { scroll-snap-align: start; }
              """;

        // pb-base-style: pagination mechanics only, never changes. pb-user-style: the live typography/
        // theme layer (Step 6 of the parent WebView redesign) - baked in here for the *initial* render
        // of each chapter (no NavigationCompleted race to wait out), then re-injected in place via
        // InvokeScript by PushTypographyCss whenever a setting changes without needing a full reload.
        string document = $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <style id="pb-base-style">
              html, body { margin: 0; padding: 0; height: 100vh; overflow: hidden; }
              #pb-viewport { width: 100vw; height: 100vh; overflow: hidden; position: relative; box-sizing: border-box; }
              #pb-content { box-sizing: border-box; }
              {{modeCss}}
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
            <body><div id="pb-viewport"><div id="pb-content">{{chapterHtml}}</div></div></body>
            <script>window.pbPageMode = {{(UseColumnPaging ? "true" : "false")}};</script>
            <script>{{HighlightScript}}</script>
            </html>
            """;

        ReaderWebView.NavigateToString(document);
    }

    /// <summary>
    /// True: attempt CSS-column + <c>transform</c> paging (see <see cref="PushCurrentChapterHtml"/>'s
    /// own doc comment). False: fall back to vertical scroll dressed with CSS scroll-snap.
    ///
    /// <b>Flipped to false 2026-09-07 after real on-screen verification</b> (docs/superpowers/specs/
    /// 2026-09-07-books-reader-pagination-and-position-fix-design.md's disclosed fallback chain): the
    /// transform-based attempt hit the *same* next-column-bleeding-in-at-the-right-edge symptom as both
    /// of the original design's scrollLeft-based attempts, screenshotted live in a real Dune EPUB. This
    /// rules out the leading theory (that a native-scroll-triggered repaint bug in this specific WebView
    /// hosting mode was the cause, since transform never scrolls at all) - the defect is evidently
    /// something more fundamental about how `Avalonia.Controls.WebView` composites CSS multi-column
    /// layout + `overflow: hidden` clipping in general, not the page-turn mechanism. Three independently
    /// reasoned attempts (vw sizing, exact-pixel sizing, transform-instead-of-scroll) failing with the
    /// identical visual symptom is a strong enough signal to stop trying blind - this needs live
    /// devtools access neither this nor any prior session has had. Per the design's own fallback chain,
    /// this item is now closed out on vertical scroll (dressed with CSS scroll-snap), same status as the
    /// magnifier: permanently declined, not silently left half-done.
    /// </summary>
    private const bool UseColumnPaging = false;

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
        // Pagination + position capture/restore (docs/superpowers/specs/2026-09-07-books-reader-
        // pagination-and-position-fix-design.md). window.pbPageMode (set inline by
        // BookReaderScreen.axaml.cs's PushCurrentChapterHtml, mirroring UseColumnPaging) picks between
        // two independent page-turn implementations: column+transform (no native scroll, sidestepping
        // the scrollLeft-driven repaint bug the previous two pagination attempts hit) or plain vertical
        // scroll on #pb-viewport (the disclosed fallback). Both report position the same way, through
        // pbReportPosition - the topmost-visible-block check (getBoundingClientRect against
        // #pb-viewport's own rect) works identically regardless of which mode moved the viewport.
        function pbApplyPagination() {
            if (!window.pbPageMode) return;
            var viewport = document.getElementById('pb-viewport');
            var content = document.getElementById('pb-content');
            if (!viewport || !content) return;
            var width = viewport.clientWidth;
            var height = viewport.clientHeight;
            content.style.columnWidth = width + 'px';
            content.style.height = height + 'px';
            window.pbPageWidth = width;
            window.pbPageCount = Math.max(1, Math.round(content.scrollWidth / width));
            if (typeof window.pbCurrentPage !== 'number') window.pbCurrentPage = 0;
            window.pbCurrentPage = Math.max(0, Math.min(window.pbCurrentPage, window.pbPageCount - 1));
            content.style.transform = 'translateX(-' + (window.pbCurrentPage * width) + 'px)';
        }
        function pbSetPageTransform() {
            var content = document.getElementById('pb-content');
            if (content) content.style.transform = 'translateX(-' + (window.pbCurrentPage * window.pbPageWidth) + 'px)';
        }
        function pbNextPageColumn() {
            if (window.pbCurrentPage + 1 >= window.pbPageCount) return 'end';
            window.pbCurrentPage += 1;
            pbSetPageTransform();
            var fraction = 'moved:' + (window.pbPageCount > 1 ? window.pbCurrentPage / (window.pbPageCount - 1) : 0);
            pbReportPosition();
            return fraction;
        }
        function pbPreviousPageColumn() {
            if (window.pbCurrentPage <= 0) return 'start';
            window.pbCurrentPage -= 1;
            pbSetPageTransform();
            var fraction = 'moved:' + (window.pbPageCount > 1 ? window.pbCurrentPage / (window.pbPageCount - 1) : 0);
            pbReportPosition();
            return fraction;
        }
        function pbNextPageScroll() {
            var viewport = document.getElementById('pb-viewport');
            if (!viewport) return 'end';
            var step = viewport.clientHeight;
            var before = viewport.scrollTop;
            viewport.scrollTop += step;
            if (viewport.scrollTop <= before) return 'end';
            var maxScroll = viewport.scrollHeight - step;
            var result = 'moved:' + (maxScroll > 0 ? (viewport.scrollTop / maxScroll) : 0);
            pbReportPosition();
            return result;
        }
        function pbPreviousPageScroll() {
            var viewport = document.getElementById('pb-viewport');
            if (!viewport) return 'start';
            var step = viewport.clientHeight;
            var before = viewport.scrollTop;
            viewport.scrollTop -= step;
            if (viewport.scrollTop >= before) return 'start';
            var maxScroll = viewport.scrollHeight - step;
            var result = 'moved:' + (maxScroll > 0 ? (viewport.scrollTop / maxScroll) : 0);
            pbReportPosition();
            return result;
        }
        window.pbNextPage = function () { return window.pbPageMode ? pbNextPageColumn() : pbNextPageScroll(); };
        window.pbPreviousPage = function () { return window.pbPageMode ? pbPreviousPageColumn() : pbPreviousPageScroll(); };
        function pbFindTopmostVisibleBlock() {
            var viewport = document.getElementById('pb-viewport');
            if (!viewport) return null;
            var vRect = viewport.getBoundingClientRect();
            var blocks = document.querySelectorAll('#pb-content [id^="pb-p"]');
            for (var i = 0; i < blocks.length; i++) {
                var r = blocks[i].getBoundingClientRect();
                if (r.bottom > vRect.top && r.top < vRect.bottom && r.right > vRect.left && r.left < vRect.right) {
                    return { blockId: blocks[i].id, excerpt: (blocks[i].textContent || '').trim().slice(0, 140) };
                }
            }
            return null;
        }
        function pbReportPosition() {
            var found = pbFindTopmostVisibleBlock();
            if (!found) return;
            var fraction = 0;
            if (window.pbPageMode) {
                fraction = window.pbPageCount > 1 ? window.pbCurrentPage / (window.pbPageCount - 1) : 0;
            } else {
                var v = document.getElementById('pb-viewport');
                var max = v ? v.scrollHeight - v.clientHeight : 0;
                fraction = v && max > 0 ? v.scrollTop / max : 0;
            }
            invokeCSharpAction(JSON.stringify({ type: 'position', blockId: found.blockId, fraction: fraction, excerpt: found.excerpt }));
        }
        var pbPositionDebounceTimer = null;
        function pbSchedulePositionCapture() {
            if (pbPositionDebounceTimer) clearTimeout(pbPositionDebounceTimer);
            pbPositionDebounceTimer = setTimeout(pbReportPosition, 500);
        }
        // Restore ("jump to a saved position") - called by BookReaderScreen.axaml.cs after resume-on-
        // load, a bookmark jump, or a highlight jump. Column mode: temporarily clears the transform to
        // read the target's natural (untransformed) layout position - transform never affects layout,
        // only paint, so this is a same-tick read with no visible flash - then computes which page
        // that offset falls in and jumps there directly, since CSS multi-column layout exposes no
        // "which column is this element in" API. Scroll mode: plain scrollIntoView.
        window.pbScrollToBlock = function (blockId) {
            if (!blockId) return;
            var target = document.getElementById(blockId);
            if (!target) return;
            if (!window.pbPageMode) {
                target.scrollIntoView({ block: 'start' });
                pbReportPosition();
                return;
            }
            var content = document.getElementById('pb-content');
            var savedTransform = content.style.transform;
            content.style.transform = 'none';
            var contentLeft = content.getBoundingClientRect().left;
            var targetLeft = target.getBoundingClientRect().left;
            content.style.transform = savedTransform;
            var naturalOffset = targetLeft - contentLeft;
            window.pbCurrentPage = Math.max(0, Math.min(window.pbPageCount - 1, Math.floor(naturalOffset / window.pbPageWidth)));
            pbSetPageTransform();
            pbReportPosition();
        };
        window.addEventListener('resize', function () {
            if (window.pbPageMode) pbApplyPagination();
        });
        if (window.pbPageMode) {
            pbApplyPagination();
        } else {
            var pbViewportEl = document.getElementById('pb-viewport');
            if (pbViewportEl) pbViewportEl.addEventListener('scroll', pbSchedulePositionCapture);
        }
        pbReportPosition();
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
        // Font-size/spacing changes reflow the chapter's content, changing how many columns it spans
        // in paged mode - re-run pbApplyPagination right after so window.pbPageCount/pbCurrentPage
        // stay correct instead of drifting stale until the next explicit page-turn (docs/superpowers/
        // specs/2026-09-07-books-reader-pagination-and-position-fix-design.md). A no-op in scroll mode
        // (pbApplyPagination itself checks window.pbPageMode).
        string script = "var el = document.getElementById('pb-user-style'); if (el) el.textContent = " + JsonSerializer.Serialize(css) +
            "; if (window.pbApplyPagination) window.pbApplyPagination();";
        _ = ReaderWebView.InvokeScript(script);
    }

    // "moved:<fraction>" reports the new page/scroll position after a successful within-chapter
    // page-turn; "end"/"start" means the WebView is already at that chapter's boundary - the caller
    // falls back to the ViewModel's chapter-advance commands in that case. window.pbNextPage/
    // pbPreviousPage (HighlightScript) pick the column-transform or vertical-scroll implementation
    // based on window.pbPageMode - see PushCurrentChapterHtml's own doc comment.
    private const string NextPageScript = "window.pbNextPage();";

    private const string PreviousPageScript = "window.pbPreviousPage();";

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
        FlushPendingScroll();
    }

    /// <summary>
    /// The ViewModel wants the reading pane scrolled/paged to a specific block - resume-on-load, a
    /// bookmark jump, a highlight jump (docs/superpowers/specs/2026-09-07-books-reader-pagination-and-
    /// position-fix-design.md). Fired *after* <c>RecomputeCurrentPage()</c> already ran, so
    /// <see cref="_isPageReady"/> correctly reflects whether a chapter reload is now in flight (set
    /// false by <see cref="PushCurrentChapterHtml"/> if <c>CurrentChapterHtml</c> changed) or the
    /// target chapter was already the one on screen (no reload, DOM still there right now).
    /// </summary>
    private void OnPositionRestoreRequested(string? blockId)
    {
        _pendingScrollBlockId = blockId;
        if (_isPageReady)
        {
            FlushPendingScroll();
        }

        // else: OnReaderNavigationCompleted flushes it once the new chapter finishes loading.
    }

    private void FlushPendingScroll()
    {
        if (string.IsNullOrEmpty(_pendingScrollBlockId))
        {
            _pendingScrollBlockId = null;
            return;
        }

        string blockId = _pendingScrollBlockId;
        _pendingScrollBlockId = null;
        _ = ReaderWebView.InvokeScript($"if (window.pbScrollToBlock) window.pbScrollToBlock({JsonSerializer.Serialize(blockId)});");
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
            // Deferred: the scrim itself lives inside the drawer/sheet Popup's own content, so
            // closing synchronously here would detach that content mid pointer-event-route -
            // Avalonia's detach walk crashes with an ArgumentOutOfRangeException (see
            // Paperbunkr.App.Controls.SuggestBox.Commit for the fully diagnosed case).
            Dispatcher.UIThread.Post(() =>
            {
                vm.CloseTocCommand.Execute(null);
                vm.CloseFontSheetCommand.Execute(null);
                vm.CloseBookmarksCommand.Execute(null);
                vm.CloseHighlightsCommand.Execute(null);
                vm.CloseSearchCommand.Execute(null);
            });
        }

        e.Handled = true;
    }

}
