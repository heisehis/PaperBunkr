using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.ContinuityScreen;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventSmartFillRenderTests : ContinuityScreenTestBase
{
    [Theory]
    [InlineData("default", 750)] [InlineData("daylight", 750)] [InlineData("matrix", 750)]
    [InlineData("default", 1100)] [InlineData("daylight", 1100)] [InlineData("matrix", 1100)]
    public void ComparisonKeepsFooterVisibleAndScrollsLongCandidates(string theme, int width)
    {
        Theming.FoundationStyleTests.WithFoundation(theme, () =>
        {
            var vm = new EventSmartFillViewModel { IsOpen = true, Title = "Compare sources · Timestorm 2009/2099",
                Status = "Two alternatives need your choice. Existing values stay unchanged until you apply a selection.",
                Sources = "GCD: snapshot 2026-09-15 · 6 original issues; one on-sale date missing" };
            foreach (string provider in new[] { "Metron", "ComicVine", "GCD" })
                vm.Candidates.Add(new() { Evidence = new(EventMetadataField.Description,
                    "Present-day heroes encounter their counterparts from the future. " + string.Join(" ", Enumerable.Repeat("An event synopsis with enough context to compare the source text.", 12)), provider, "1", "Arc overview"),
                    Current = "Current: Collected Editions / Non-U.S. Editions", Protection = "Existing value is protected; select to replace" });
            vm.Candidates.Add(new() { Evidence = new(EventMetadataField.EndDate,
                JsonSerializer.Serialize(new EventPublicationDate(2009, 8, Provisional: true, Basis: "GCD on-sale / key date")), "GCD", "members", "Derived from original issues"), Current = "Current: Empty" });
            var view = new EventSmartFillOverlay { DataContext = vm };
            var window = new Window { Width = width, Height = 700, Content = view };
            window.Show(); RunLayout(window); TestDispatcher.Drain(); RunLayout(window);
            var footer = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.ApplySelectedCommand);
            Assert.True(footer.IsEffectivelyVisible);
            var point = footer.TranslatePoint(default, window); Assert.NotNull(point);
            Assert.InRange(point!.Value.Y + footer.Bounds.Height, 0, window.Bounds.Height);
            var body = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PART_BodyScroller");
            Assert.True(body.Extent.Height > body.Viewport.Height);
            if (Environment.GetEnvironmentVariable("PB_RENDER_OUT") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                using var image = new RenderTargetBitmap(new PixelSize(width, 700), new Vector(96, 96));
                image.Render(window); image.Save(Path.Combine(folder, $"event-smart-fill-{theme}-{width}.png"));
            }
            window.Close();
        });
    }
}
