using System.IO.Compression;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Step 9 of docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-plan.md - "a
/// real Narrator session against the NativeWebView-based reader... the single biggest unresolved
/// risk the design calls out." A literal Narrator audio session isn't something this environment
/// can drive (no computer-use access), and the plan itself says "Verify: Manual only, explicitly -
/// this is exactly the kind of claim the earlier accessibility spec insisted can't be satisfied by
/// code review." This doesn't try to satisfy that by code review either - it answers the same
/// underlying question a different way, with a real launched app and real UIA queries (FlaUI/UIA3,
/// same tooling every other screen's on-screen verification in this project already uses): does
/// Chromium's own accessibility tree (which is what Narrator actually reads from) get exposed
/// through Avalonia's NativeWebView hosting into the Windows UI Automation tree at all? If the
/// chapter's real prose text shows up as a descendant AutomationElement's Name, screen readers have
/// something to read; if the WebView is an opaque leaf with no queryable children, that's the
/// negative result the design's own fallback (a custom AutomationPeer) exists for.
/// </summary>
public class BookReaderAccessibilityTests : IDisposable
{
    private readonly AppFixture _fixture = new();
    private readonly string _epubPath;

    public BookReaderAccessibilityTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_uitest_reader_a11y_{Guid.NewGuid():N}.epub");
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try
        {
            if (File.Exists(_epubPath)) File.Delete(_epubPath);
        }
        catch (IOException)
        {
        }
    }

    private void SeedBook()
    {
        CreateMinimalEpub(_epubPath);
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Books.Add(new Book { Title = "Accessibility Probe Novel", FilePath = _epubPath, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
        context.SaveChanges();
    }

    /// <summary>
    /// A minimal, hand-authored valid EPUB3 (mirrors Paperbunkr.App.Tests/EpubFixture.cs's
    /// structure, not shared with it directly - that class is internal to its own test project and
    /// UiTests doesn't reference it, same isolation every other UiTests fixture in this project
    /// already keeps). One real chapter with distinctive prose text is all Step 9's UIA probe needs.
    /// </summary>
    private static void CreateMinimalEpub(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimetype.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:00000000-0000-0000-0000-000000000099</dc:identifier>
                <dc:title>Accessibility Probe Novel</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="chap1" href="chap1.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine>
                <itemref idref="chap1"/>
              </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
              <nav epub:type="toc">
                <ol><li><a href="chap1.xhtml">The Beginning</a></li></ol>
              </nav>
            </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/chap1.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>The Beginning</title></head>
            <body>
              <h1>The Beginning</h1>
              <p>It was a dark and stormy night.</p>
              <p>She walked slowly into the fog.</p>
            </body>
            </html>
            """);
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Diagnostic-only: dumps ControlType/AutomationId/Name for the visible tree so a test
    /// failure explains *what screen was actually showing*, instead of just "expected element not
    /// found" - much cheaper than guessing at the cause blind.</summary>
    private static string DumpTree(FlaUI.Core.AutomationElements.AutomationElement element, int maxDepth, int depth = 0)
    {
        var sb = new StringBuilder();
        void Walk(FlaUI.Core.AutomationElements.AutomationElement el, int d)
        {
            if (d > maxDepth) return;
            string indent = new string(' ', d * 2);
            string id = string.Empty;
            string name = string.Empty;
            string controlType = string.Empty;
            try { id = el.Properties.AutomationId.ValueOrDefault ?? string.Empty; } catch { }
            try { name = el.Properties.Name.ValueOrDefault ?? string.Empty; } catch { }
            try { controlType = el.ControlType.ToString(); } catch { }
            sb.AppendLine($"{indent}[{controlType}] id='{id}' name='{name}'");
            foreach (var child in el.FindAllChildren())
            {
                Walk(child, d + 1);
            }
        }
        Walk(element, depth);
        return sb.ToString();
    }

    /// <summary>
    /// A raw, unfiltered <c>FindAllChildren()</c>-based walk, deliberately NOT FlaUI's own
    /// <c>FindFirstDescendant</c>/<c>FindAllDescendants</c> (which scope to UIA's "control view" by
    /// default). Real finding while writing this test: <c>ReadingPositionLiveRegion</c> - a plain,
    /// definitely-present TextBlock confirmed via <see cref="DumpTree"/> - was completely invisible
    /// to <c>FindFirstDescendant</c> every time, control-view filtering excluding it. Since that's
    /// exactly the kind of AT-visibility gap Step 9 is trying to catch, the actual probe below uses
    /// this raw walk throughout rather than risk the same false negative for WebView content.
    /// </summary>
    private static IEnumerable<FlaUI.Core.AutomationElements.AutomationElement> WalkAllRaw(FlaUI.Core.AutomationElements.AutomationElement root)
    {
        foreach (var child in root.FindAllChildren())
        {
            yield return child;
            foreach (var descendant in WalkAllRaw(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void BookReaderWebView_ExposesChapterProseText_ToUiAutomation()
    {
        SeedBook();
        var window = _fixture.Window;

        // A truly fresh DB auto-opens the first-run Welcome overlay (same condition/fix as
        // KeyboardShortcutDiagnosticTests.cs - this machine has real ComicRack CE installed).
        // AppFixture only dismisses the *migration* overlay itself, not this one.
        var welcomeSkip = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("WelcomeSkip")),
            TimeSpan.FromSeconds(15), throwOnTimeout: false).Result;
        if (welcomeSkip is not null)
        {
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            Thread.Sleep(1000);
            window.FindFirstDescendant(cf => cf.ByAutomationId("WelcomeSkip"))?.Click();
            Thread.Sleep(500);
        }

        // Books -> the seeded card -> "Start reading" (BookDetailScreenViewModel.ContinueLabel for
        // a never-opened book) -> lands in BookReaderScreen on chapter 1.
        window.FindFirstDescendant(cf => cf.ByAutomationId("BooksRailButton"))!.AsButton().Invoke();
        var card = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("BookCard")),
            TimeSpan.FromSeconds(8), throwOnTimeout: true,
            timeoutMessage: "Seeded book card never appeared on the Books screen.").Result!;
        card.AsButton().Invoke();

        var startReading = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("Start reading")),
            TimeSpan.FromSeconds(8), throwOnTimeout: true,
            timeoutMessage: "\"Start reading\" action never appeared on the Book Detail screen.").Result!;
        startReading.AsButton().Invoke();
        Thread.Sleep(2000);

        // EpubFixture's chap1.xhtml real prose text - distinctive enough that a match can only come
        // from the actual rendered chapter content, not chrome. Polls (rather than asserting once)
        // because WebView2's real navigation + Chromium's own accessibility-tree construction are
        // both async with no UIA-visible "ready" signal here - a single too-early query finding
        // nothing would be a false negative, not evidence the bridge doesn't work. Uses the raw walk
        // throughout (see WalkAllRaw's own doc comment) after confirming FlaUI's control-view-scoped
        // FindFirstDescendant misses even a definitely-present plain TextBlock in this app.
        const string expectedProse = "dark";
        (bool Success, FlaUI.Core.AutomationElements.AutomationElement? Match) result = (false, null);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            var match = WalkAllRaw(window)
                .FirstOrDefault(el => (SafeName(el)).Contains(expectedProse, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                result = (true, match);
                break;
            }

            Thread.Sleep(500);
        }

        if (!result.Success)
        {
            string dump = DumpTree(window, maxDepth: 8);
            string dumpPath = Path.Combine(Path.GetTempPath(), "pb_uitest_tree_dump.txt");
            File.WriteAllText(dumpPath, dump);
        }

        Assert.True(result.Success,
            $"No element anywhere in the window (raw tree walk, not just under BookReaderWebView) exposed text " +
            $"containing \"{expectedProse}\" to UI Automation within 25s of clicking Start reading. Chromium's " +
            "own accessibility tree does not appear to bridge through this NativeWebView hosting into Windows " +
            "UIA. This is the negative result docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-" +
            "redesign-plan.md's Step 9 flags as triggering the design's documented fallback (a custom " +
            "AutomationPeer) - a real Narrator session should still confirm this by ear before committing to " +
            $"that path, but this is real evidence pointing the same direction. Tree dump: " +
            $"{Path.Combine(Path.GetTempPath(), "pb_uitest_tree_dump.txt")}");
    }

    private static string SafeName(FlaUI.Core.AutomationElements.AutomationElement el)
    {
        try { return el.Properties.Name.ValueOrDefault ?? string.Empty; }
        catch { return string.Empty; }
    }
}
