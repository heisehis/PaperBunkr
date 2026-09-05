using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Views;

/// <summary>
/// Real DrawThumbnailOverlay-hook anchor (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
/// hooks-plan.md §12) - an extra paint pass on the Library grid's per-issue tile, mirroring
/// <see cref="AsyncCoverImage"/>'s off-UI-thread decode/cache shape exactly, layered as a sibling
/// <see cref="Image"/> on top of the cover.
///
/// CE invokes this hook live, per paint, with a raw GDI+ <c>Graphics</c> callback
/// (<c>CoverViewItem.DrawCustomThumbnailOverlay</c> in <c>_reference/ComicRackCE/ComicRack/
/// MainForm.cs</c>) - no Avalonia equivalent exists, and firing a Roslyn script synchronously on
/// every tile repaint in a virtualized grid would be a real perf hazard (this codebase already
/// fought hard to get cover decode off the UI thread - see <see cref="AsyncCoverImage"/>'s own doc
/// comment). Paperbunkr's adaptation, consistent with every other icon method's <c>byte[]?</c> PNG
/// convention (<c>PaperbunkrApplication.GetComicPage</c>/icon methods): the script returns overlay
/// PNG bytes (with alpha) once per issue, decoded off-thread and cached for the process lifetime -
/// not re-run on every repaint, and not live-updating if the plugin's answer would change based on
/// runtime state. Null return = no overlay, same null-means-nothing convention as the icon methods.
///
/// Only wired into the primary "Tiles" (Poster grid) view template - not every one of Library's
/// other view modes' cover images - since no live plugin implements this hook yet; extending
/// coverage to every view mode is a mechanical follow-up once one does.
/// </summary>
public sealed class AsyncPluginOverlayImage
{
    private AsyncPluginOverlayImage()
    {
    }

    /// <summary>Set once from <c>App.axaml.cs</c> after <see cref="PluginHostService.Initialize"/> - this is a static attached-property helper class, not a ViewModel, so there's no natural <c>AttachHost</c> point (same reasoning as <see cref="Services.LibraryFolderScanner.PluginHost"/>).</summary>
    public static PluginHostService? PluginHost { get; set; }

    public static readonly AttachedProperty<int?> SourceIdProperty =
        AvaloniaProperty.RegisterAttached<AsyncPluginOverlayImage, Image, int?>("SourceId");

    private static readonly AttachedProperty<long> GenerationProperty =
        AvaloniaProperty.RegisterAttached<AsyncPluginOverlayImage, Image, long>("Generation");

    /// <summary>Process-lifetime cache, issue id -&gt; decoded overlay (or null - no overlay). Never invalidated: this hook is explicitly not live/per-paint (see class doc comment).</summary>
    private static readonly ConcurrentDictionary<int, Bitmap?> s_cache = new();

    private static readonly ConcurrentDictionary<int, Task<Bitmap?>> s_inflight = new();

    static AsyncPluginOverlayImage()
    {
        SourceIdProperty.Changed.AddClassHandler<Image>(OnSourceIdChanged);
    }

    public static void SetSourceId(Image target, int? value) => target.SetValue(SourceIdProperty, value);

    public static int? GetSourceId(Image target) => target.GetValue(SourceIdProperty);

    private static void OnSourceIdChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        long generation = image.GetValue(GenerationProperty) + 1;
        image.SetValue(GenerationProperty, generation);

        if (e.NewValue is not int issueId || PluginHost is null)
        {
            image.Source = null;
            return;
        }

        if (s_cache.TryGetValue(issueId, out var cached))
        {
            image.Source = cached;
            return;
        }

        image.Source = null;

        var decode = s_inflight.GetOrAdd(issueId, id => Task.Run(() => DecodeOverlayAsync(id)));
        decode.ContinueWith(
            t =>
            {
                s_inflight.TryRemove(issueId, out _);
                var result = t.IsCompletedSuccessfully ? t.Result : null;
                Dispatcher.UIThread.Post(() => Apply(image, issueId, generation, result));
            },
            TaskScheduler.Default);
    }

    /// <summary>Runs every enabled DrawThumbnailOverlay command in registration order, using the first non-null PNG result - same "first wins" convention as <see cref="PluginHostService.RunParseComicPathHookAsync"/>.</summary>
    private static async Task<Bitmap?> DecodeOverlayAsync(int issueId)
    {
        if (PluginHost is null)
        {
            return null;
        }

        var commands = PluginHost.Engine.GetCommands(PluginHooks.DrawThumbnailOverlay).ToList();
        if (commands.Count == 0)
        {
            return null;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is null)
        {
            return null;
        }

        foreach (var command in commands)
        {
            if (command.Environment is null)
            {
                continue;
            }

            var result = await PluginHost.RunCommandAsync(command, new DrawThumbnailOverlayHookGlobals { Environment = command.Environment, Book = issue }).ConfigureAwait(false);
            if (!result.Success || result.ReturnValue is not byte[] bytes)
            {
                continue;
            }

            try
            {
                return new Bitmap(new MemoryStream(bytes));
            }
            catch
            {
                // Malformed PNG bytes from a broken script - fall through to the next command / no overlay, same "never crash the host" rule every other hook invocation follows.
            }
        }

        return null;
    }

    /// <summary>Internal for direct testing of the generation guard, same shape as <see cref="AsyncCoverImage.Apply"/>.</summary>
    internal static void Apply(Image image, int issueId, long generation, Bitmap? decoded)
    {
        if (image.GetValue(GenerationProperty) != generation)
        {
            return;
        }

        s_cache[issueId] = decoded;
        image.Source = decoded;
    }
}
