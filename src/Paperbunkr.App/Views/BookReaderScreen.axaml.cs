using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

public partial class BookReaderScreen : UserControl
{
    private BookReaderScreenViewModel? _viewModel;

    private readonly IInputService _input = InputServiceLocator.Current;
    private readonly AttachedInputRegistration _bookInput;

    public BookReaderScreen()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        _bookInput = new AttachedInputRegistration(this, InputScope.BookReader, OnBookInputAction, service: _input, focusRoot: () => this);
        _input.BindingsChanged += (_, _) => PushBoundKeys();

        // Default WebView2 UserDataFolder is next to the EXE. A per-machine install (installer/
        // Installer.iss DefaultDirName={autopf} → Program Files) is not writable by a standard,
        // non-elevated user, so CoreWebView2Environment creation throws
        // UnauthorizedAccessException/E_ACCESSDENIED at WebView2HwndAdapter.CreateBuilder before
        // any navigation happens - confirmed via a real user crash report on a Program-Files
        // install. Redirect to a per-user, always-writable folder instead.
        ReaderWebView.EnvironmentRequested += OnReaderWebViewEnvironmentRequested;

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

    private static void OnReaderWebViewEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is WindowsWebView2EnvironmentRequestedEventArgs winArgs)
        {
            winArgs.UserDataFolder = Path.Combine(Paperbunkr.Data.AppDataPaths.LocalRoot, "WebView2");
        }
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
            _viewModel.RulerMoveRequested -= OnRulerMoveRequested;
            _viewModel.SpeakingBlockChanged -= OnSpeakingBlockChanged;
        }

        _viewModel = DataContext as BookReaderScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
            _viewModel.Highlights.CollectionChanged += OnHighlightsCollectionChanged;
            _viewModel.PositionRestoreRequested += OnPositionRestoreRequested;
            _viewModel.RulerMoveRequested += OnRulerMoveRequested;
            _viewModel.SpeakingBlockChanged += OnSpeakingBlockChanged;
            PushCurrentChapterHtml();
        }
    }

    private void OnHighlightsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ApplyHighlightsToWebView();
        PushMarginNotes();
    }

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

        if (type == "jumpBack")
        {
            _viewModel.TryJumpBack();
            return;
        }

        if (type == "pointer")
        {
            // The page now fills the window (books redesign §5.1), so pointer moves over it never reach RootGrid: the page script forwards
            // them (throttled), with whether the pointer is near the top or bottom edge, which is what brings the clusters back.
            _viewModel.NotifyPointerActivity(root.TryGetProperty("edge", out var edge) && edge.GetBoolean());
            return;
        }

        if (type == "noteTap")
        {
            if (root.TryGetProperty("id", out var noteId) && noteId.TryGetInt32(out int highlightId)
                && _viewModel.Highlights.FirstOrDefault(h => h.Id == highlightId) is { } tapped)
            {
                _viewModel.GoToHighlightCommand.Execute(tapped);
            }

            return;
        }

        if (type == "autoScrollStopped")
        {
            _viewModel.OnAutoScrollStoppedByPage();
            return;
        }

        if (type == "speak")
        {
            switch (root.TryGetProperty("op", out var speakOp) ? speakOp.GetString() : null)
            {
                case "stop": _viewModel.ToggleSpeakCommand.Execute(null); break;
                case "pause": _viewModel.ToggleSpeakPauseCommand.Execute(null); break;
                case "faster": _viewModel.SpeakFasterCommand.Execute(null); break;
                case "slower": _viewModel.SpeakSlowerCommand.Execute(null); break;
            }

            return;
        }

        if (type == "autoScroll")
        {
            switch (root.TryGetProperty("op", out var opProp) ? opProp.GetString() : null)
            {
                case "toggle": _viewModel.ToggleAutoScrollCommand.Execute(null); break;
                case "faster": _viewModel.AutoScrollFasterCommand.Execute(null); break;
                case "slower": _viewModel.AutoScrollSlowerCommand.Execute(null); break;
            }

            return;
        }

        if (type == "page")
        {
            // Paged mode: which screen page of the unit is showing, for the footer.
            _viewModel.OnScreenPage(root.GetProperty("page").GetInt32(), root.GetProperty("count").GetInt32());
            return;
        }

        if (type == "turn")
        {
            // A page turn the page asked for (wheel, the ruler running off the page): the same path as the Next/Previous keys, so unit boundaries work.
            if (root.GetProperty("dir").GetInt32() > 0)
            {
                _ = NextPageAsync();
            }
            else
            {
                _ = PreviousPageAsync();
            }

            return;
        }

        if (type == "key")
        {
            // A key the page swallowed because the input service has it bound in the book reader (see HighlightScript's keydown listener): resolve it like any other key.
            string jsKey = root.TryGetProperty("key", out var keyProp) ? keyProp.GetString() ?? string.Empty : string.Empty;
            if (WebKeyMap.TryFromJsKey(jsKey, out var key))
            {
                bool Held(string name) => root.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;
                _input.ProcessKey(key, WebKeyMap.ModifiersFor(Held("ctrl"), Held("alt"), Held("shift"), Held("meta")));
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
        // Books redesign §5.1-5.2: the page edges and folio follow the place; the clusters follow the chrome state.
        if (e.PropertyName == nameof(BookReaderScreenViewModel.ProgressLabel))
        {
            PushPaperState();
        }
        else if (e.PropertyName == nameof(BookReaderScreenViewModel.IsChromeVisible))
        {
            SyncClusters();
        }

        if (e.PropertyName == nameof(BookReaderScreenViewModel.CurrentChapterHtml))
        {
            PushCurrentChapterHtml();
        }
        else if (e.PropertyName is nameof(BookReaderScreenViewModel.HasJumpBack) or nameof(BookReaderScreenViewModel.JumpBackLabel))
        {
            PushJumpBackChip();
        }
        else if (e.PropertyName == nameof(BookReaderScreenViewModel.Layout))
        {
            // Re-render the unit in the other layout and come back to the same block (spec §5).
            string? block = _viewModel!.CurrentBlockId;
            PushCurrentChapterHtml();
            OnPositionRestoreRequested(block);
        }
        else if (e.PropertyName == nameof(BookReaderScreenViewModel.IsHighlightPopupOpen))
        {
            SetHighlightPopupOpen(_viewModel!.IsHighlightPopupOpen);
        }
        else if (e.PropertyName == nameof(BookReaderScreenViewModel.IsRulerOn))
        {
            PushRuler();
        }
        else if (e.PropertyName is nameof(BookReaderScreenViewModel.IsSpeaking) or nameof(BookReaderScreenViewModel.IsSpeakingPaused) or nameof(BookReaderScreenViewModel.SpeechRate))
        {
            PushSpeechPill();
        }
        else if (e.PropertyName is nameof(BookReaderScreenViewModel.IsAutoScrolling) or nameof(BookReaderScreenViewModel.AutoScrollSpeed))
        {
            PushAutoScroll();
        }
    }

    /// <summary>Applies the ruler state to the page; line height follows the font size and spacing, so it is re-sent when those change too (see <see cref="OnSettingsPropertyChanged"/>).</summary>
    private void PushRuler()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        string on = _viewModel.IsRulerOn ? "true" : "false";
        string lineHeight = _viewModel.Settings.LineHeightPixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = ReaderWebView.InvokeScript($"if (window.pbSetRuler) window.pbSetRuler({on}, {lineHeight});");
    }

    private void PushAutoScroll()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        string on = _viewModel.IsAutoScrolling ? "true" : "false";
        string pps = _viewModel.AutoScrollPixelsPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = ReaderWebView.InvokeScript($"if (window.pbSetAutoScroll) window.pbSetAutoScroll({on}, {pps}, {_viewModel.AutoScrollSpeed});");
    }

    private void PushSpeechPill()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        string on = _viewModel.IsSpeaking ? "true" : "false";
        string paused = _viewModel.IsSpeakingPaused ? "true" : "false";
        _ = ReaderWebView.InvokeScript($"if (window.pbSetSpeechPill) window.pbSetSpeechPill({on}, {paused}, {_viewModel.SpeechRate});");
    }

    private void OnSpeakingBlockChanged(string? blockId)
    {
        if (_isPageReady)
        {
            _ = ReaderWebView.InvokeScript($"if (window.pbSetSpeaking) window.pbSetSpeaking({JsonSerializer.Serialize(blockId)});");
        }
    }

    private void OnRulerMoveRequested(int lines)
    {
        if (_isPageReady)
        {
            _ = ReaderWebView.InvokeScript($"if (window.pbRulerMove) window.pbRulerMove({lines});");
        }
    }

    /// <summary>Shows or hides the in-page jump-back chip to match the view model; re-run after each chapter load, since a new document starts without it.</summary>
    private void PushJumpBackChip()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        string label = _viewModel.HasJumpBack ? _viewModel.JumpBackLabel ?? string.Empty : string.Empty;
        _ = ReaderWebView.InvokeScript(
            $"if (window.pbSetJumpChip) window.pbSetJumpChip({JsonSerializer.Serialize(label)}, {JsonSerializer.Serialize(_viewModel.JumpBackHint)});");
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
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PushTypographyCss();
        if (e.PropertyName is nameof(BookReaderSettings.FontSize) or nameof(BookReaderSettings.LineSpacing))
        {
            PushRuler();
            PushAutoScroll();
        }
    }

    /// <summary>
    /// Loads the current reading unit into the page (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §5.1). One DOM shape for
    /// both layouts: <c>#pb-viewport</c> is the fixed frame, <c>#pb-content</c> the flowing text. Paged mode lays the text out as one ordinary column and
    /// lets <see cref="HighlightScript"/>'s <c>pbApplyPagination</c> cut it into whole-line pages; scroll mode scrolls the viewport. CSS columns are not used:
    /// three attempts at them (2026-09-02 to 09-07) all showed the next column bleeding in at the edge inside this WebView.
    /// </summary>
    private void PushCurrentChapterHtml()
    {
        string? chapterHtml = _viewModel?.CurrentChapterHtml;
        if (string.IsNullOrEmpty(chapterHtml) || _viewModel is null)
        {
            return;
        }

        _isPageReady = false;

        // Paged: the viewport never scrolls (pages are a translate plus a clip) and a picture is never taller than a page, so it always fits on one.
        bool paged = _viewModel.IsPaged;
        string modeCss = paged
            ? """
              #pb-content { will-change: transform; }
              #pb-content img, #pb-content svg { max-height: 85vh; object-fit: contain; }
              """
            : """
              #pb-viewport { overflow-y: auto; }
              """;
        var motion = Controls.MotionTokens.Get(Theming.Motion.MotionRole.Replace);
        int motionMs = motion.AllowsTransform ? (int)motion.Fade.TotalMilliseconds : 0;

        // pb-base-style: pagination mechanics only, never changes. pb-user-style: the live typography/
        // theme layer (Step 6 of the parent WebView redesign) - baked in here for the *initial* render
        // of each chapter (no NavigationCompleted race to wait out), then re-injected in place via
        // InvokeScript by PushTypographyCss whenever a setting changes without needing a full reload.
        string document = $$"""
            <!DOCTYPE html>
            <html lang="{{Services.Reader.BookReaderCss.LanguageAttribute(_viewModel.BookLanguage)}}">
            <head>
            <meta charset="utf-8" />
            <style id="pb-base-style">
              html, body { margin: 0; padding: 0; height: 100vh; overflow: hidden; }
              #pb-viewport { width: 100vw; height: 100vh; overflow: hidden; position: relative; box-sizing: border-box; }
              #pb-edge-left, #pb-edge-right, #pb-folio, #pb-notes { display: none; }
              #pb-content { box-sizing: border-box; }
              {{modeCss}}
              img { max-width: 100%; height: auto; }
            </style>
            <style id="pb-user-style">{{BuildTypographyCss(_viewModel.Settings)}}</style>
            <style id="pb-chapter-ornament">
              /* Reflowable EPUB chapter headings get a restrained opening treatment. This stays
                 in document order and inherits the active reading theme's foreground color. */
              #pb-content > h1:first-child, #pb-content > h2:first-child {
                margin-top: 1.8em; padding-top: .8em; border-top: 1px solid currentColor;
                letter-spacing: .035em;
              }
              #pb-content > h1:first-child::before, #pb-content > h2:first-child::before {
                content: "✦"; display: block; font-size: .72em; opacity: .62;
                margin-bottom: .6em; text-align: center;
              }
              #pb-content > h1:first-child::first-letter, #pb-content > h2:first-child::first-letter {
                float: left; font-size: 2.2em; line-height: .9; padding-right: .12em;
              }
            </style>
            <style id="pb-highlight-style">
              .pb-highlight { cursor: pointer; }
              .pb-color-Yellow { background: {{HighlightColorConverter.CssRgba(BookHighlightColor.Yellow, 0.55)}}; }
              .pb-color-Green { background: {{HighlightColorConverter.CssRgba(BookHighlightColor.Green, 0.55)}}; }
              .pb-color-Blue { background: {{HighlightColorConverter.CssRgba(BookHighlightColor.Blue, 0.55)}}; }
              .pb-color-Pink { background: {{HighlightColorConverter.CssRgba(BookHighlightColor.Pink, 0.55)}}; }
              /* Note marker (docs/superpowers/specs/2026-10-27-books-slice3c-reader-annotations-design.md section 2): a small dot at the end of a highlight that has a note.
                 Absolutely positioned generated content, so it takes no room in the line, is not selectable and is not part of the text; a click on it is a click on the span. */
              .pb-has-note { position: relative; }
              .pb-has-note::after {
                content: ''; position: absolute; right: -0.4em; top: -0.2em; width: 0.55em; height: 0.55em;
                border-radius: 50%; background: currentColor; opacity: 0.7; pointer-events: auto;
              }
              /* The paragraph being read aloud (Slice 4d): a soft tint from the theme's own text colour, no reflow. */
              .pb-speaking { background: color-mix(in srgb, var(--pb-fg) 14%, transparent); border-radius: 4px; }
              .pb-color-Yellow.pb-has-note::after { background: rgb(255, 193, 7); }
              .pb-color-Green.pb-has-note::after { background: rgb(76, 175, 80); }
              .pb-color-Blue.pb-has-note::after { background: rgb(33, 150, 243); }
              .pb-color-Pink.pb-has-note::after { background: rgb(233, 30, 99); }
            </style>
            </head>
            <body><div id="pb-paper"><div id="pb-edge-left"></div><div id="pb-viewport"><div id="pb-content">{{chapterHtml}}</div></div><div id="pb-edge-right"></div></div><div id="pb-folio"></div><div id="pb-notes"></div></body>
            <script>window.pbPageMode = {{(paged ? "true" : "false")}}; window.pbMotionMs = {{motionMs}};</script>
            <script>window.pbBoundKeys = {{BoundKeysJson()}};</script>
            <script>{{HighlightScript}}</script>
            <script>{{PaperScript}}</script>
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
                span.className = 'pb-highlight pb-color-' + h.color + (h.hasNote ? ' pb-has-note' : '');
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
        // Jump-back chip (docs/superpowers/specs/2026-10-27-books-slice3a-reader-jumpback-session-design.md). It lives in the page because the WebView composites above every Avalonia element,
        // so an Avalonia chip over the reading pane would never show. Inverse of the theme (--pb-fg on --pb-bg) keeps it readable in every theme; a click posts back to the host.
        window.pbSetJumpChip = function (label, hint) {
            var chip = document.getElementById('pb-jump-chip');
            if (!label) { if (chip) chip.remove(); return; }
            if (!chip) {
                chip = document.createElement('div');
                chip.id = 'pb-jump-chip';
                chip.setAttribute('role', 'button');
                chip.style.cssText = 'position:fixed;left:50%;bottom:20px;transform:translateX(-50%);z-index:50;padding:8px 16px;border-radius:12px;cursor:pointer;'
                    + 'font:13px/1.2 system-ui,sans-serif;background:var(--pb-fg);color:var(--pb-bg);opacity:.92;box-shadow:0 2px 10px rgba(0,0,0,.3);user-select:none;white-space:nowrap;';
                chip.addEventListener('click', function (e) {
                    e.stopPropagation();
                    invokeCSharpAction(JSON.stringify({ type: 'jumpBack' }));
                });
                document.body.appendChild(chip);
            }
            chip.textContent = '↩ ' + label + (hint ? '  ' + hint : '');
        };
        // Reading aids (docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md section 4). Both live in the page for the same airspace reason as the chip.
        // Ruler: two dim bands (theme background at ~78 %) above and below a clear band two lines tall that follows the pointer; pointer-events are off so it never eats a click.
        // Mirrors BookRulerMath.Band / Step.
        window.pbRulerState = { on: false, lh: 27, top: -1 };
        function pbRulerLayout() {
            var s = window.pbRulerState, vh = window.innerHeight, h = s.lh * 2;
            var topBand = document.getElementById('pb-ruler-top'), bottomBand = document.getElementById('pb-ruler-bottom');
            if (!s.on) { if (topBand) topBand.remove(); if (bottomBand) bottomBand.remove(); return; }
            if (s.top < 0) s.top = Math.max(0, (vh - h) / 2);
            s.top = Math.max(0, Math.min(s.top, vh - h));
            var dim = 'position:fixed;left:0;right:0;z-index:40;pointer-events:none;background:color-mix(in srgb, var(--pb-bg) 78%, transparent);';
            if (!topBand) { topBand = document.createElement('div'); topBand.id = 'pb-ruler-top'; document.body.appendChild(topBand); }
            if (!bottomBand) { bottomBand = document.createElement('div'); bottomBand.id = 'pb-ruler-bottom'; document.body.appendChild(bottomBand); }
            topBand.style.cssText = dim + 'top:0;height:' + s.top + 'px;';
            bottomBand.style.cssText = dim + 'top:' + (s.top + h) + 'px;bottom:0;';
        }
        window.pbSetRuler = function (on, lineHeightPx) {
            window.pbRulerState.on = !!on;
            window.pbRulerState.lh = lineHeightPx > 0 ? lineHeightPx : 27;
            pbRulerLayout();
        };
        window.pbRulerMove = function (lines) {
            window.pbRulerState.top += lines * window.pbRulerState.lh;
            pbRulerLayout();
        };
        document.addEventListener('mousemove', function (e) {
            var s = window.pbRulerState;
            if (!s.on) return;
            s.top = e.clientY - s.lh;
            pbRulerLayout();
        });
        // Auto-scroll: requestAnimationFrame loop on #pb-viewport (the scroll container in the paging-off mode this reader ships in). Stops at the end of the chapter (it never turns the
        // chapter) and on any wheel, press, touch or key the host has not bound, then tells the host so its state follows the page.
        window.pbAutoScroll = { on: false, pps: 0, last: 0, acc: 0 };
        function pbAutoScrollNotify(on) {
            var pill = document.getElementById('pb-autoscroll-pill');
            if (!on) { if (pill) pill.remove(); return; }
            if (!pill) {
                pill = document.createElement('div');
                pill.id = 'pb-autoscroll-pill';
                pill.style.cssText = 'position:fixed;right:20px;bottom:20px;z-index:50;display:flex;gap:4px;align-items:center;padding:4px 8px;border-radius:12px;'
                    + 'font:13px/1.2 system-ui,sans-serif;background:var(--pb-fg);color:var(--pb-bg);opacity:.92;box-shadow:0 2px 10px rgba(0,0,0,.3);user-select:none;';
                [['slower', '−'], ['toggle', '⏸'], ['faster', '+']].forEach(function (b) {
                    var btn = document.createElement('span');
                    btn.textContent = b[1];
                    btn.setAttribute('role', 'button');
                    btn.style.cssText = 'cursor:pointer;padding:4px 8px;';
                    btn.addEventListener('click', function (e) {
                        e.stopPropagation();
                        invokeCSharpAction(JSON.stringify({ type: 'autoScroll', op: b[0] }));
                    });
                    pill.appendChild(btn);
                });
                var label = document.createElement('span');
                label.id = 'pb-autoscroll-speed';
                label.style.cssText = 'padding:0 6px;min-width:1ch;text-align:center;';
                pill.insertBefore(label, pill.children[2]);
                document.body.appendChild(pill);
            }
        }
        function pbAutoScrollFrame(ts) {
            var a = window.pbAutoScroll;
            if (!a.on) return;
            var viewport = document.getElementById('pb-viewport');
            if (!viewport) { a.on = false; return; }
            if (a.last) {
                a.acc += a.pps * Math.min(0.1, (ts - a.last) / 1000);
                var whole = Math.floor(a.acc);
                if (whole > 0) { viewport.scrollTop += whole; a.acc -= whole; }
            }
            a.last = ts;
            if (viewport.scrollTop + viewport.clientHeight >= viewport.scrollHeight - 1) {
                window.pbSetAutoScroll(false, 0, 0);
                invokeCSharpAction(JSON.stringify({ type: 'autoScrollStopped' }));
                return;
            }
            requestAnimationFrame(pbAutoScrollFrame);
        }
        window.pbSetAutoScroll = function (on, pixelsPerSecond, speed) {
            var a = window.pbAutoScroll, wasOn = a.on;
            a.on = !!on;
            a.pps = pixelsPerSecond;
            pbAutoScrollNotify(a.on);
            var label = document.getElementById('pb-autoscroll-speed');
            if (label) label.textContent = speed;
            if (a.on && !wasOn) { a.last = 0; a.acc = 0; requestAnimationFrame(pbAutoScrollFrame); }
        };
        function pbUserStoppedAutoScroll() {
            if (!window.pbAutoScroll.on) return;
            window.pbSetAutoScroll(false, 0, 0);
            invokeCSharpAction(JSON.stringify({ type: 'autoScrollStopped' }));
        }
        ['wheel', 'mousedown', 'touchstart'].forEach(function (name) {
            document.addEventListener(name, function (e) {
                if (e.target && e.target.closest && e.target.closest('#pb-autoscroll-pill')) return;
                pbUserStoppedAutoScroll();
            }, true);
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Control' || e.key === 'Shift' || e.key === 'Alt' || e.key === 'Meta') return;
            var norm = (e.ctrlKey ? 'ctrl+' : '') + (e.altKey ? 'alt+' : '') + (e.shiftKey ? 'shift+' : '') + (e.metaKey ? 'meta+' : '')
                + (e.key.length === 1 ? e.key.toLowerCase() : e.key);
            if (window.pbBoundKeys && window.pbBoundKeys.indexOf(norm) >= 0) return;
            pbUserStoppedAutoScroll();
        }, true);
        // Read aloud (docs/superpowers/specs/2026-10-27-books-slice4d-read-aloud-design.md section 4): the paragraph being spoken gets .pb-speaking and is scrolled into view; the pill
        // (pause, stop, slower, faster) lives in the page for the same airspace reason as the chip. Each button posts back to the host, which owns the state.
        window.pbSetSpeaking = function (blockId) {
            document.querySelectorAll('.pb-speaking').forEach(function (el) { el.classList.remove('pb-speaking'); });
            if (!blockId) return;
            var el = document.getElementById(blockId);
            if (!el) return;
            el.classList.add('pb-speaking');
            el.scrollIntoView({ block: 'start', behavior: 'smooth' });
        };
        window.pbSetSpeechPill = function (on, paused, rate) {
            var pill = document.getElementById('pb-speech-pill');
            if (!on) { if (pill) pill.remove(); return; }
            if (!pill) {
                pill = document.createElement('div');
                pill.id = 'pb-speech-pill';
                pill.style.cssText = 'position:fixed;left:20px;bottom:20px;z-index:50;display:flex;gap:4px;align-items:center;padding:4px 8px;border-radius:12px;'
                    + 'font:13px/1.2 system-ui,sans-serif;background:var(--pb-fg);color:var(--pb-bg);opacity:.92;box-shadow:0 2px 10px rgba(0,0,0,.3);user-select:none;';
                [['slower', '−', null], ['pause', '⏸', 'pb-speech-pause'], ['stop', '■', null], ['faster', '+', null]].forEach(function (b) {
                    var btn = document.createElement('span');
                    btn.textContent = b[1];
                    if (b[2]) btn.id = b[2];
                    btn.setAttribute('role', 'button');
                    btn.style.cssText = 'cursor:pointer;padding:4px 8px;';
                    btn.addEventListener('click', function (e) {
                        e.stopPropagation();
                        invokeCSharpAction(JSON.stringify({ type: 'speak', op: b[0] }));
                    });
                    pill.appendChild(btn);
                });
                var label = document.createElement('span');
                label.id = 'pb-speech-rate';
                label.style.cssText = 'padding:0 6px;min-width:2ch;text-align:center;';
                pill.insertBefore(label, pill.children[3]);
                document.body.appendChild(pill);
            }
            var pause = document.getElementById('pb-speech-pause');
            if (pause) pause.textContent = paused ? '▶' : '⏸';
            var rateLabel = document.getElementById('pb-speech-rate');
            if (rateLabel) rateLabel.textContent = (rate > 0 ? '+' : '') + rate;
        };
        // Keyboard while the WebView has focus (docs/superpowers/specs/2026-10-03-input-service-design.md section 9). Avalonia never sees these keys - the same reason tap-to-toggle-chrome
        // and the old Ctrl+Shift+W needed a JS bridge - so the page forwards them instead of deciding for itself which ones turn the page. window.pbBoundKeys is the set of keys
        // the input service currently has bound in the book reader (pushed with each chapter and again whenever bindings change, see BoundKeysJson/PushBoundKeys): the page swallows
        // exactly those and posts each one back for the service to resolve, so page turns and the "where am I?" announcement follow Preferences > Keyboard Shortcuts here too.
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Control' || e.key === 'Shift' || e.key === 'Alt' || e.key === 'Meta') return;
            var norm = (e.ctrlKey ? 'ctrl+' : '') + (e.altKey ? 'alt+' : '') + (e.shiftKey ? 'shift+' : '') + (e.metaKey ? 'meta+' : '')
                + (e.key.length === 1 ? e.key.toLowerCase() : e.key);
            if (!window.pbBoundKeys || window.pbBoundKeys.indexOf(norm) < 0) return;
            e.preventDefault();
            invokeCSharpAction(JSON.stringify({ type: 'key', key: e.key, ctrl: e.ctrlKey, alt: e.altKey, shift: e.shiftKey, meta: e.metaKey }));
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
        // Paged and scroll layouts (docs/superpowers/specs/2026-10-27-books-reader-paged-scroll-modes-design.md §5). window.pbPageMode is true in paged mode,
        // set inline by PushCurrentChapterHtml from the reader's Layout. Paged mode never uses CSS columns (three attempts showed the next column bleeding in,
        // 2026-09-07): the chapter is laid out as one normal column, pbApplyPagination reads where the line boxes really fall and builds a list of page edges so
        // no line is ever cut, and a page is shown by translating #pb-content up and clipping #pb-viewport below the page's last whole line. Scroll mode is a
        // plain vertical scroll of #pb-viewport. Both report position through pbReportPosition and answer pbNextPage/pbPreviousPage with "moved:<fraction>",
        // "end" or "start"; at "end"/"start" the host opens the neighbouring reading unit.
        window.pbPages = [{ top: 0, bottom: 0, offset: 0 }];
        window.pbCurrentPage = 0;
        window.pbAnchorBlock = null; // the block last reported as the reader's place; a rebuild after a font or size change comes back to it
        function pbViewport() { return document.getElementById('pb-viewport'); }
        function pbContent() { return document.getElementById('pb-content'); }
        // Measures with the page transform off (a transform moves every rect) and puts it back.
        function pbUntransformed(fn) {
            var content = pbContent(), viewport = pbViewport();
            var savedTransform = content.style.transform, savedClip = viewport.style.clipPath;
            content.style.transform = 'none';
            viewport.style.clipPath = 'none';
            try { return fn(content.getBoundingClientRect().top); }
            finally { content.style.transform = savedTransform; viewport.style.clipPath = savedClip; }
        }
        // Every line box (text) and replaced element in content coordinates, top to bottom.
        function pbLineBoxes(origin) {
            var content = pbContent(), boxes = [], range = document.createRange(), node;
            var walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, null);
            while (node = walker.nextNode()) {
                if (!node.textContent.trim()) continue;
                range.selectNodeContents(node);
                var rects = range.getClientRects();
                for (var i = 0; i < rects.length; i++) {
                    if (rects[i].height > 0) boxes.push({ top: rects[i].top - origin, bottom: rects[i].bottom - origin });
                }
            }
            content.querySelectorAll('img, svg, hr, table, video').forEach(function (el) {
                var r = el.getBoundingClientRect();
                if (r.height > 0) boxes.push({ top: r.top - origin, bottom: r.bottom - origin });
            });
            boxes.sort(function (a, b) { return a.top - b.top || a.bottom - b.bottom; });
            return boxes;
        }
        function pbElementTop(el) {
            return pbUntransformed(function (origin) { return el.getBoundingClientRect().top - origin; });
        }
        function pbPageOfElement(el) {
            if (!el) return window.pbCurrentPage;
            var top = pbElementTop(el), page = 0;
            for (var i = 0; i < window.pbPages.length; i++) { if (window.pbPages[i].top <= top + 1) page = i; }
            return page;
        }
        function pbShowPage(k, dir) {
            var content = pbContent(), viewport = pbViewport();
            if (!content || !viewport || !window.pbPages.length) return;
            k = Math.max(0, Math.min(window.pbPages.length - 1, k));
            var page = window.pbPages[k];
            window.pbCurrentPage = k;
            // Pages after the first keep the chapter's top padding as their top margin; the clip hides the previous page's lines in it and the part of a
            // line below the page's last whole line.
            content.style.transform = 'translateY(' + (page.offset - page.top) + 'px)';
            viewport.style.clipPath = 'inset(' + page.offset + 'px 0 ' + Math.max(0, viewport.clientHeight - page.offset - (page.bottom - page.top)) + 'px 0)';
            if (dir && window.pbMotionMs > 0 && content.animate) {
                content.animate([{ opacity: 0, translate: (dir * 6) + 'px 0' }, { opacity: 1, translate: '0 0' }],
                    { duration: window.pbMotionMs, easing: 'cubic-bezier(0.2, 0, 0, 1)' });
            }
            invokeCSharpAction(JSON.stringify({ type: 'page', page: k, count: window.pbPages.length }));
        }
        // Builds the page edges. A page holds whole lines only: it closes before the first line box that would cross the bottom (less the bottom padding),
        // provided that box starts below everything already on the page (so two rects on one line never split). The next page starts at that box and is
        // drawn below the same top padding the first page has. One box taller than a page gets a page of its own. Keeps the reader's place.
        window.pbApplyPagination = function () {
            if (!window.pbPageMode) return;
            var content = pbContent(), viewport = pbViewport();
            if (!content || !viewport) return;
            var keep = window.pbAnchorBlock;
            var height = viewport.clientHeight;
            var style = getComputedStyle(content);
            var padTop = parseFloat(style.paddingTop) || 0, padBottom = parseFloat(style.paddingBottom) || 0;
            var pages = pbUntransformed(function (origin) {
                var boxes = pbLineBoxes(origin), list = [], top = 0, bottom = 0, offset = 0, any = false;
                for (var i = 0; i < boxes.length; i++) {
                    var b = boxes[i];
                    if (any && offset + (b.bottom - top) > height - padBottom && b.top >= bottom - 0.5) {
                        list.push({ top: top, bottom: bottom, offset: offset });
                        offset = padTop;
                        top = b.top;
                        bottom = b.bottom;
                    } else {
                        bottom = Math.max(bottom, b.bottom);
                    }
                    any = true;
                }
                list.push({ top: top, bottom: Math.max(top, bottom), offset: offset });
                return list;
            });
            window.pbPages = pages;
            pbShowPage(keep && document.getElementById(keep) ? pbPageOfElement(document.getElementById(keep)) : window.pbCurrentPage, 0);
        };
        function pbPagedResult() { return 'moved:' + (window.pbPages.length > 1 ? window.pbCurrentPage / (window.pbPages.length - 1) : 0); }
        function pbNextPagePaged() {
            if (window.pbCurrentPage + 1 >= window.pbPages.length) return 'end';
            pbShowPage(window.pbCurrentPage + 1, 1);
            pbReportPosition();
            return pbPagedResult();
        }
        function pbPreviousPagePaged() {
            if (window.pbCurrentPage <= 0) return 'start';
            pbShowPage(window.pbCurrentPage - 1, -1);
            pbReportPosition();
            return pbPagedResult();
        }
        function pbNextPageScroll() {
            var viewport = pbViewport();
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
            var viewport = pbViewport();
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
        window.pbNextPage = function () { return window.pbPageMode ? pbNextPagePaged() : pbNextPageScroll(); };
        window.pbPreviousPage = function () { return window.pbPageMode ? pbPreviousPagePaged() : pbPreviousPageScroll(); };
        // The block the reader is at: paged, the first block that starts on the page (else the one running into it from the previous page); scroll, the
        // first block visible in the viewport.
        function pbFindTopmostVisibleBlock() {
            var viewport = pbViewport();
            if (!viewport) return null;
            var vRect = viewport.getBoundingClientRect();
            var visibleBottom = vRect.bottom;
            var visibleTop = vRect.top;
            if (window.pbPageMode && window.pbPages.length) {
                var page = window.pbPages[window.pbCurrentPage];
                visibleTop = vRect.top + page.offset;
                visibleBottom = visibleTop + (page.bottom - page.top);
            }
            var blocks = document.querySelectorAll('#pb-content [id^="pb-p"]');
            var spanning = null;
            for (var i = 0; i < blocks.length; i++) {
                var r = blocks[i].getBoundingClientRect();
                if (r.bottom > visibleTop && r.top < visibleBottom && r.right > vRect.left && r.left < vRect.right) {
                    if (!window.pbPageMode || r.top >= visibleTop - 1) {
                        return { blockId: blocks[i].id, excerpt: (blocks[i].textContent || '').trim().slice(0, 200) };
                    }
                    if (!spanning) spanning = blocks[i];
                }
            }
            return spanning ? { blockId: spanning.id, excerpt: (spanning.textContent || '').trim().slice(0, 200) } : null;
        }
        function pbReportPosition() {
            var found = pbFindTopmostVisibleBlock();
            if (!found) return;
            window.pbAnchorBlock = found.blockId;
            var fraction = 0;
            if (window.pbPageMode) {
                fraction = window.pbPages.length > 1 ? window.pbCurrentPage / (window.pbPages.length - 1) : 0;
            } else {
                var v = pbViewport();
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
        // Restore ("jump to a saved position") - resume, bookmark, highlight, search, contents row and Go to page. 'pb:last' lands on the unit's end (going
        // back into the previous chapter). Paged: the page holding the element's top; scroll: scrollIntoView.
        window.pbScrollToBlock = function (blockId) {
            if (!blockId) return;
            var viewport = pbViewport();
            if (blockId === 'pb:last') {
                if (window.pbPageMode) pbShowPage(window.pbPages.length - 1, 0);
                else if (viewport) viewport.scrollTop = viewport.scrollHeight;
                pbReportPosition();
                return;
            }
            var target = document.getElementById(blockId);
            if (!target) return;
            window.pbAnchorBlock = blockId;
            if (window.pbPageMode) pbShowPage(pbPageOfElement(target), 0);
            else target.scrollIntoView({ block: 'start' });
            pbReportPosition();
        };
        // Read aloud and the ruler turn pages in paged mode (spec §5.5); the host owns the unit boundary, so a turn is posted back as a 'turn' message.
        var pbSpeakingInScroll = window.pbSetSpeaking;
        window.pbSetSpeaking = function (blockId) {
            if (!window.pbPageMode || !blockId) { pbSpeakingInScroll(blockId); return; }
            document.querySelectorAll('.pb-speaking').forEach(function (el) { el.classList.remove('pb-speaking'); });
            var el = document.getElementById(blockId);
            if (!el) return;
            el.classList.add('pb-speaking');
            var page = pbPageOfElement(el);
            if (page !== window.pbCurrentPage) { pbShowPage(page, page > window.pbCurrentPage ? 1 : -1); pbReportPosition(); }
        };
        window.pbRulerMove = function (lines) {
            var s = window.pbRulerState;
            s.top += lines * s.lh;
            if (window.pbPageMode && window.pbPages.length) {
                var page = window.pbPages[window.pbCurrentPage], visible = page.offset + page.bottom - page.top;
                if (s.top + s.lh * 2 > visible + 1) {
                    s.top = 0;
                    invokeCSharpAction(JSON.stringify({ type: 'turn', dir: 1 }));
                } else if (s.top < 0) {
                    s.top = -1; // re-centred by pbRulerLayout on the new page
                    invokeCSharpAction(JSON.stringify({ type: 'turn', dir: -1 }));
                }
            }
            pbRulerLayout();
        };
        // Mouse wheel. Paged: one page per gesture (a gesture ends after 250 ms without wheel events, so a touchpad flick is one page). Scroll: a wheel past
        // either end opens the neighbouring unit, but only once the page has rested at that end for 400 ms, so one long fling never skips a chapter.
        var pbWheelLast = 0, pbEdgeSince = Date.now(), pbLastTurn = 0;
        function pbAtEdge(dir) {
            var v = pbViewport();
            if (!v) return false;
            return dir > 0 ? v.scrollTop + v.clientHeight >= v.scrollHeight - 1 : v.scrollTop <= 0;
        }
        document.addEventListener('wheel', function (e) {
            if (e.ctrlKey || !e.deltaY) return;
            var dir = e.deltaY > 0 ? 1 : -1, now = Date.now();
            if (window.pbPageMode) {
                e.preventDefault();
                var continuing = now - pbWheelLast < 250;
                pbWheelLast = now;
                if (!continuing) invokeCSharpAction(JSON.stringify({ type: 'turn', dir: dir }));
                return;
            }
            if (pbAtEdge(dir) && now - pbEdgeSince > 400 && now - pbLastTurn > 400) {
                pbLastTurn = now;
                invokeCSharpAction(JSON.stringify({ type: 'turn', dir: dir }));
            }
        }, { passive: false });
        window.addEventListener('resize', function () { if (window.pbPageMode) window.pbApplyPagination(); });
        if (window.pbPageMode) {
            window.pbApplyPagination();
            // Images and web fonts can change the layout after this script runs: rebuild the pages once they are in.
            window.addEventListener('load', function () { window.pbApplyPagination(); });
            if (document.fonts && document.fonts.ready) document.fonts.ready.then(function () { window.pbApplyPagination(); });
        } else {
            var pbViewportEl = pbViewport();
            if (pbViewportEl) {
                pbViewportEl.addEventListener('scroll', function () {
                    pbSchedulePositionCapture();
                    if (!pbAtEdge(1) && !pbAtEdge(-1)) pbEdgeSince = 0;
                    else if (!pbEdgeSince) pbEdgeSince = Date.now();
                });
            }
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

        string json = Services.Reader.BookHighlightPayload.Build(_viewModel.GetCurrentChapterHighlights());
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
        // The CSS itself is built by BookReaderCss so every rule is unit-testable without the WebView.
        return Services.Reader.BookReaderCss.Build(new Services.Reader.BookReaderCssInput(
            Services.Reader.BookReaderCss.FontStack(settings.FontFamilyOption, settings.CustomFont),
            settings.FontSize,
            settings.LineHeightMultiplier,
            settings.CharacterSpacing,
            settings.WordSpacing,
            settings.ParagraphSpacing,
            settings.PageMargin,
            ToCssColor(settings.Background),
            ToCssColor(settings.Foreground),
            settings.Justify,
            settings.Hyphenate,
            settings.UsePublisherStyles,
            // Books redesign §5.1: paper on a desk, except OLED Black and High Contrast, which keep the whole window as page.
            Paper: settings.Theme is not (BookTheme.OledBlack or BookTheme.HighContrast),
            Accent: Application.Current?.TryGetResource("PbAccentColor", null, out object? accent) == true && accent is Color accentColor
                ? $"#{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}"
                : null,
            UiFont: "'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif"));
    }

    /// <summary>
    /// The paper's page script (books redesign §5.1): page-edge widths and the folio (set from C# as the place changes), margin notes beside
    /// the lines they belong to (when the window is wide enough; below that the existing dot after a noted highlight stays the only mark), and
    /// pointer forwarding so moving to the top or bottom edge brings the reader's clusters back (the WebView swallows pointer events).
    /// </summary>
    private const string PaperScript = """
        (function () {
            window.pbSetEdges = function (left, right) {
                var l = document.getElementById('pb-edge-left'), r = document.getElementById('pb-edge-right');
                if (l) l.style.width = left + 'px';
                if (r) r.style.width = right + 'px';
            };
            window.pbSetFolio = function (text) {
                var f = document.getElementById('pb-folio');
                if (f) f.textContent = text || '';
            };
            window.pbNotes = [];
            window.pbSetNotes = function (notes) { window.pbNotes = notes || []; window.pbRenderNotes(); };
            window.pbRenderNotes = function () {
                var host = document.getElementById('pb-notes');
                if (!host) return;
                host.innerHTML = '';
                if (window.innerWidth < 1200 || !window.pbNotes.length) return;
                var viewport = document.getElementById('pb-viewport');
                var vr = viewport.getBoundingClientRect();
                var lastBottom = -1;
                window.pbNotes.forEach(function (n) {
                    var el = document.getElementById(n.block);
                    if (!el) return;
                    var r = el.getBoundingClientRect();
                    if (r.bottom < vr.top || r.top > vr.bottom) return;
                    var top = Math.max(r.top, lastBottom + 6);
                    var card = document.createElement('div');
                    card.className = 'pb-margin-note';
                    card.style.top = top + 'px';
                    card.style.setProperty('--pb-note', n.color);
                    var quote = document.createElement('b');
                    quote.textContent = n.excerpt;
                    card.appendChild(quote);
                    card.appendChild(document.createElement('br'));
                    card.appendChild(document.createTextNode(n.note));
                    card.addEventListener('click', function () { invokeCSharpAction(JSON.stringify({ type: 'noteTap', id: n.id })); });
                    host.appendChild(card);
                    lastBottom = top + card.offsetHeight;
                });
            };
            var content = document.getElementById('pb-content'), viewport = document.getElementById('pb-viewport');
            if (content && window.MutationObserver) {
                new MutationObserver(function () { window.pbRenderNotes(); }).observe(content, { attributes: true, attributeFilter: ['style'] });
            }
            if (viewport) viewport.addEventListener('scroll', function () { window.pbRenderNotes(); }, { passive: true });
            // A click on the desk around the paper toggles the clusters too (the text's own click handler covers the page itself).
            document.addEventListener('click', function (e) {
                var t = e.target;
                var desk = t === document.body || t === document.documentElement
                    || (t && (t.id === 'pb-paper' || t.id === 'pb-viewport' || t.id === 'pb-edge-left' || t.id === 'pb-edge-right'));
                if (desk) invokeCSharpAction(JSON.stringify({ type: 'contentTap' }));
            });
            window.addEventListener('resize', function () { window.pbRenderNotes(); });
            var lastPointer = 0;
            document.addEventListener('mousemove', function (e) {
                var now = Date.now();
                var edge = e.clientY < 80 || e.clientY > window.innerHeight - 80;
                if (!edge && now - lastPointer < 400) return;
                if (edge && now - lastPointer < 150) return;
                lastPointer = now;
                invokeCSharpAction(JSON.stringify({ type: 'pointer', edge: edge }));
            }, { passive: true });
        })();
        """;

    /// <summary>Pushes the page-edge widths and the folio for the current place (books redesign §5.1). Edges only in paged mode, 2-24 px,
    /// proportional to the book read before and after this page.</summary>
    private void PushPaperState()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        double fraction = Math.Clamp(_viewModel.ProgressFraction, 0, 1);
        double left = _viewModel.IsPaged ? Services.Reader.PageEdgeMath.Width(fraction) : 0;
        double right = _viewModel.IsPaged ? Services.Reader.PageEdgeMath.Width(1 - fraction) : 0;
        string folio = _viewModel.FolioLabel ?? string.Empty;
        string script = FormattableString.Invariant($"if (window.pbSetEdges) window.pbSetEdges({left:0.#}, {right:0.#}); if (window.pbSetFolio) window.pbSetFolio({JsonSerializer.Serialize(folio)});");
        _ = ReaderWebView.InvokeScript(script);
    }

    /// <summary>Pushes this unit's noted highlights for the margin (books redesign §5.1).</summary>
    private void PushMarginNotes()
    {
        if (_viewModel is null || !_isPageReady)
        {
            return;
        }

        var notes = _viewModel.Highlights
            .Where(h => h.HasNote && h.ChapterIndex == _viewModel.CurrentFileIndex)
            .Select(h => new
            {
                id = h.Id,
                block = h.BlockId,
                note = h.Note,
                excerpt = h.Excerpt.Length > 60 ? h.Excerpt[..60] + "…" : h.Excerpt,
                color = HighlightColorConverter.CssRgba(h.Color, 1),
            });
        _ = ReaderWebView.InvokeScript("if (window.pbSetNotes) window.pbSetNotes(" + JsonSerializer.Serialize(notes) + ");");
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

    private async void OnClusterNextRequested(object? sender, RoutedEventArgs e) => await NextPageAsync();

    private async void OnClusterPreviousRequested(object? sender, RoutedEventArgs e) => await PreviousPageAsync();

    // --- floating clusters (books redesign §5.2) ---

    private Reader.ClusterWindowController? _navigateWindow;
    private Reader.ClusterWindowController? _actionsWindow;
    private Reader.ClusterWindowController? _pageWindow;

    /// <summary>Shows the three clusters, each in its own owned window, while the chrome is visible and the screen is on screen; hides them otherwise.</summary>
    private void SyncClusters()
    {
        if (_navigateWindow is null)
        {
            _navigateWindow = new Reader.ClusterWindowController(NavigateCluster, () => RootGrid, Services.Reader.ClusterAnchor.TopLeft);
            _actionsWindow = new Reader.ClusterWindowController(ActionsCluster, () => RootGrid, Services.Reader.ClusterAnchor.TopRight);
            _pageWindow = new Reader.ClusterWindowController(PageCluster, () => RootGrid, Services.Reader.ClusterAnchor.BottomCenter);
            foreach (var cluster in new Control[] { NavigateCluster, ActionsCluster, PageCluster })
            {
                cluster.PointerEntered += (_, _) => _viewModel?.SetPointerOverChrome(true);
                cluster.PointerExited += (_, _) => _viewModel?.SetPointerOverChrome(false);
            }
        }

        bool show = _viewModel is { IsChromeVisible: true } && IsEffectivelyVisible && VisualRoot is not null;
        if (!show)
        {
            _viewModel?.SetPointerOverChrome(false); // a closing window may not report the pointer leaving
        }
        if (show != _navigateWindow.IsOpen)
        {
            Services.DiagnosticsService.LogMilestone(
                $"BookReader clusters: {(show ? "show" : "hide")} (chrome {_viewModel?.IsChromeVisible}, visible {IsEffectivelyVisible}, attached {VisualRoot is not null}).");
        }

        _navigateWindow.SetOpen(show);
        _actionsWindow!.SetOpen(show);
        _pageWindow!.SetOpen(show);
    }

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
        PushJumpBackChip();
        PushPaperState();
        PushMarginNotes();
        SyncClusters(); // the screen is certainly on screen once its page has loaded
        PushRuler();
        PushAutoScroll();
        PushSpeechPill();
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

    private OverlayHostWindow? _highlightWindow;

    /// <summary>
    /// Shows the highlight colour / note / Look up card in an owned window under the selection. A Popup is embedded in the window (Win32PlatformOptions.OverlayPopups) and so is covered by the
    /// NativeWebView; an owned window is not. Clicking anywhere else (the page, another window) deactivates it, which dismisses it like the old light-dismiss.
    /// </summary>
    private void SetHighlightPopupOpen(bool open)
    {
        if (!open)
        {
            Dispatcher.UIThread.Post(CloseHighlightWindow);
            return;
        }

        if (_highlightWindow is not null || _viewModel is not { } vm || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var card = HighlightPopupCard;
        HighlightPopupHolder.Children.Remove(card);
        card.Margin = new Thickness(0);

        var window = new OverlayHostWindow { Content = card, DataContext = vm, SizeToContent = SizeToContent.WidthAndHeight };
        var anchor = vm.HighlightPopupAnchorRect;
        const double estimatedHeight = 340;
        var screen = owner.Screens.ScreenFromVisual(owner);
        double scale = owner.RenderScaling;
        var below = RootGrid.PointToScreen(new Point(anchor.X + anchor.Width / 2 - 130, anchor.Bottom + 6));
        var above = RootGrid.PointToScreen(new Point(anchor.X + anchor.Width / 2 - 130, anchor.Top - 6 - estimatedHeight));
        bool fitsBelow = screen is null || below.Y + estimatedHeight * scale <= screen.WorkingArea.Bottom;
        window.Position = fitsBelow || above.Y < 0 ? below : above;

        window.Opened += (_, _) => card.Focus();
        window.Deactivated += (_, _) =>
        {
            if (_viewModel is { IsHighlightPopupOpen: true } open && ReferenceEquals(_highlightWindow, window))
            {
                open.CancelHighlightPopupCommand.Execute(null);
            }
        };

        _highlightWindow = window;
        window.Show(owner);
    }

    private void CloseHighlightWindow()
    {
        if (_highlightWindow is not { } window)
        {
            return;
        }

        _highlightWindow = null;
        window.Content = null;
        window.Close();
        if (!HighlightPopupHolder.Children.Contains(HighlightPopupCard))
        {
            HighlightPopupHolder.Children.Add(HighlightPopupCard);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CloseHighlightWindow();
        // Leaving the reader: the cluster windows go with it.
        _navigateWindow?.SetOpen(false);
        _actionsWindow?.SetOpen(false);
        _pageWindow?.SetOpen(false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(SyncClusters, DispatcherPriority.Background);
        // A route host can show or hide this screen by an ancestor's visibility, which raises nothing here; the viewport change does.
        RootGrid.EffectiveViewportChanged -= OnRootViewportChanged;
        RootGrid.EffectiveViewportChanged += OnRootViewportChanged;
    }

    private void OnRootViewportChanged(object? sender, Avalonia.Layout.EffectiveViewportChangedEventArgs e) =>
        Dispatcher.UIThread.Post(SyncClusters, DispatcherPriority.Background);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            SyncClusters();
        }
    }

    /// <summary>Enter in the Go to page field jumps (a text box's own commit key, not a hotkey).</summary>
    private void OnGoToPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel is { } vm)
        {
            e.Handled = true;
            vm.GoToPageCommand.Execute(null);
        }
    }

    private void OnHighlightPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || _viewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.CancelHighlightPopupCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.Source is not TextBox && vm.TryLookUpSelection())
        {
            e.Handled = true;
        }
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
    /// The book reader's shortcuts, as input-service actions in the BookReader scope (docs/superpowers/specs/2026-10-03-input-service-design.md section 9): Right/PageDown/Space and
    /// Left/PageUp turn pages (a real gap found via manual testing 2026-09-02 - the reader had no keyboard page-turning at all), Ctrl+Shift+W announces the reading position. This
    /// is the Avalonia side (focus on the chrome); keys typed while the WebView has focus arrive through the page's own forwarder as a "key" message and reach the same handler.
    /// </summary>
    private async void OnBookInputAction(InputActionEventArgs e)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        switch (e.Action.Id)
        {
            case InputActionIds.BookNextPage:
                e.Handled = true;
                vm.NotifyKeyActivity();
                await NextPageAsync();
                break;
            case InputActionIds.BookPreviousPage:
                e.Handled = true;
                vm.NotifyKeyActivity();
                await PreviousPageAsync();
                break;
            case InputActionIds.BookAnnouncePosition:
                e.Handled = true;
                vm.NotifyKeyActivity();
                vm.AnnounceReadingPositionCommand.Execute(null);
                break;
            case InputActionIds.BookLookUp:
                if (vm.TryLookUpSelection())
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                }

                break;
            case InputActionIds.BookSpeakToggle:
                if (vm.IsSpeaking || vm.HasSpeechEngine())
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                    vm.ToggleSpeakCommand.Execute(null);
                }

                break;
            case InputActionIds.BookSpeakPause:
            case InputActionIds.BookSpeakFaster:
            case InputActionIds.BookSpeakSlower:
                // Only meaningful while reading aloud, so these decline otherwise (Ctrl+Up / Ctrl+Down and Shift+T keep any other meaning).
                if (vm.IsSpeaking)
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                    if (e.Action.Id == InputActionIds.BookSpeakPause) vm.ToggleSpeakPauseCommand.Execute(null);
                    else if (e.Action.Id == InputActionIds.BookSpeakFaster) vm.SpeakFasterCommand.Execute(null);
                    else vm.SpeakSlowerCommand.Execute(null);
                }

                break;
            case InputActionIds.BookRulerToggle:
                e.Handled = true;
                vm.NotifyKeyActivity();
                vm.ToggleRulerCommand.Execute(null);
                break;
            case InputActionIds.BookRulerLineUp:
            case InputActionIds.BookRulerLineDown:
                // Declines while the ruler is off so Alt+Up / Alt+Down keep any other meaning.
                if (vm.IsRulerOn)
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                    if (e.Action.Id == InputActionIds.BookRulerLineUp) vm.RulerLineUpCommand.Execute(null);
                    else vm.RulerLineDownCommand.Execute(null);
                }

                break;
            case InputActionIds.BookAutoScrollToggle:
                e.Handled = true;
                vm.NotifyKeyActivity();
                vm.ToggleAutoScrollCommand.Execute(null);
                break;
            case InputActionIds.BookAutoScrollFaster:
            case InputActionIds.BookAutoScrollSlower:
                // Speed keys only mean something while it scrolls.
                if (vm.IsAutoScrolling)
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                    if (e.Action.Id == InputActionIds.BookAutoScrollFaster) vm.AutoScrollFasterCommand.Execute(null);
                    else vm.AutoScrollSlowerCommand.Execute(null);
                }

                break;
            case InputActionIds.BookJumpBack:
                // Declines unless the chip is showing, so Alt+Left keeps any other meaning.
                if (vm.TryJumpBack())
                {
                    e.Handled = true;
                    vm.NotifyKeyActivity();
                }

                break;
        }
    }

    /// <summary>Any key counts as activity for the chrome's idle timer, whether or not it is a shortcut (the shortcuts note it themselves, since the service handles them before this runs).</summary>
    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is BookReaderScreenViewModel vm)
        {
            vm.NotifyKeyActivity();
        }
    }

    /// <summary>The keys the web page must swallow and forward: every keyboard binding of the book reader's actions, in the page's own notation (<see cref="WebKeyMap"/>), as a JSON array.</summary>
    private string BoundKeysJson()
    {
        var bindings = _input.Actions.All
            .Where(info => info.Scope.Name == InputScope.BookReader.Name)
            .SelectMany(info => _input.GetBindings(info.Action));
        return JsonSerializer.Serialize(WebKeyMap.NormalizedKeysFor(bindings));
    }

    /// <summary>Tells the page which keys are bound now, after a remap in Preferences (a chapter already on screen would otherwise keep the old set until the next page load).</summary>
    private async void PushBoundKeys()
    {
        try
        {
            await ReaderWebView.InvokeScript($"window.pbBoundKeys = {BoundKeysJson()};");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            // No page is loaded yet; the next chapter's page carries the current set in its own markup.
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
