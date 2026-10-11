using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Content Type queue's row templates (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md) built for real from <c>LibrarySection.axaml</c>:
/// a template that compiles is not one that shows its text, and the card has to open in place. Command bindings resolve against the section's view model,
/// which is not present here, so this only checks what is on screen.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContentTypeReviewRowRenderTests
{
    private static SeriesReviewItem Suggested() => new()
    {
        SeriesId = 1,
        SeriesName = "Solo Leveling",
        CurrentLabel = "Unknown",
        Suggestion = ContentType.Manhwa,
        Confidence = 0.97,
        Chips = new[] { new ContentTypeEvidenceChip("MangaBaka: manhwa", "Matched \"Solo Leveling\" (97%)"), new ContentTypeEvidenceChip("AniList: KR", "Matched \"Solo Leveling\" (100%)") },
    };

    private static (Window Window, ContentControl Host) Show(string templateKey, SeriesReviewItem item)
    {
        var section = new LibrarySection();
        var template = (IDataTemplate)section.Resources[templateKey]!;
        var host = new ContentControl { Content = item, ContentTemplate = template };
        var window = new Window { Content = host, Width = 1100, Height = 500 };
        window.Show();
        RunLayout(window);
        return (window, host);
    }

    private static IEnumerable<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);

    [Fact]
    public void ASuggestionRow_ShowsNameCurrentSuggestedChipsAndConfidence_AndItsActions()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var (window, host) = Show("ContentTypeReviewRowTemplate", Suggested());

            var texts = Texts(host).ToList();
            Assert.Contains("Solo Leveling", texts);
            Assert.Contains("Unknown", texts);
            Assert.Contains("Manhwa", texts);
            Assert.Contains("97%", texts);
            Assert.Contains("MangaBaka: manhwa · AniList: KR", texts);

            var buttons = host.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
            Assert.Contains("Accept", buttons);
            Assert.Contains("Keep", buttons);
            Assert.Contains("Not a comic", buttons);
            window.Close();
        });
    }

    [Fact]
    public void ARowWithNoSuggestion_ShowsWhyInsteadOfAnAcceptButton()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var (window, host) = Show("ContentTypeReviewRowTemplate", new SeriesReviewItem { SeriesId = 2, SeriesName = "Obscure", StatusLabel = "No match on the tracker sites" });

            Assert.Contains("No match on the tracker sites", Texts(host));
            Assert.DoesNotContain("Accept", host.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string));
            window.Close();
        });
    }

    [Fact]
    public void SelectingARow_OpensItInPlaceIntoTheCard_WithEvidenceChipsAndTheChangeButtons()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var item = Suggested();
            var (window, host) = Show("ContentTypeReviewRowTemplate", item);
            Assert.DoesNotContain("Change to", Texts(host));

            item.IsExpanded = true;
            RunLayout(window);

            var texts = Texts(host).ToList();
            Assert.Contains("Change to", texts);
            Assert.Contains("MangaBaka: manhwa", texts);
            Assert.Contains("AniList: KR", texts);
            var buttons = host.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
            Assert.Contains("Manga", buttons);
            Assert.Contains("Manhwa", buttons);
            Assert.Contains("Manhua", buttons);
            Assert.Contains("Comic", buttons);
            window.Close();
        });
    }

    [Fact]
    public void ARecentlyAutoClassifiedRow_ShowsTheChangeAndAnUndoButton()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var item = new SeriesReviewItem { SeriesId = 3, SeriesName = "Tower of God", CurrentLabel = "Unknown", Suggestion = ContentType.Manhwa, Confidence = 0.97, IsAutoClassified = true };
            var (window, host) = Show("AutoClassifiedRowTemplate", item);

            var texts = Texts(host).ToList();
            Assert.Contains("Tower of God", texts);
            Assert.Contains("Manhwa", texts);
            Assert.Contains("→", texts);
            Assert.Contains("Undo", host.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
            window.Close();
        });
    }
}
