using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Paperbunkr.App.Views;

/// <summary>Small entity thumbnails decode off-thread at display size; each image owns and releases its bitmap.</summary>
public sealed class AsyncEntityImage
{
    public static readonly AttachedProperty<string?> PathProperty = AvaloniaProperty.RegisterAttached<AsyncEntityImage, Image, string?>("Path");
    public static readonly AttachedProperty<long> RevisionProperty = AvaloniaProperty.RegisterAttached<AsyncEntityImage, Image, long>("Revision");
    private sealed class State { public long Generation; public Bitmap? Bitmap; public bool Hooked; }
    private static readonly ConditionalWeakTable<Image, State> States = new();
    static AsyncEntityImage()
    {
        PathProperty.Changed.AddClassHandler<Image>((image, _) => Load(image));
        RevisionProperty.Changed.AddClassHandler<Image>((image, _) => Load(image));
    }
    public static string? GetPath(Image image) => image.GetValue(PathProperty);
    public static void SetPath(Image image, string? path) => image.SetValue(PathProperty, path);
    public static long GetRevision(Image image) => image.GetValue(RevisionProperty);
    public static void SetRevision(Image image, long revision) => image.SetValue(RevisionProperty, revision);
    private static void Clear(Image image, State state)
    {
        state.Generation++;
        image.Source = null;
        state.Bitmap?.Dispose(); state.Bitmap = null;
    }
    private static void Load(Image image)
    {
        var state = States.GetOrCreateValue(image);
        if (!state.Hooked)
        {
            state.Hooked = true;
            image.AttachedToVisualTree += (_, _) => Load(image);
            image.DetachedFromVisualTree += (_, _) => Clear(image, state);
        }
        Clear(image, state);
        long generation = state.Generation;
        string? path = GetPath(image);
        if (string.IsNullOrWhiteSpace(path)) return;
        int width = (int)Math.Clamp(Math.Max(image.Bounds.Width, 112) * 2, 64, 600);
        _ = DecodeAsync();
        async Task DecodeAsync()
        {
            Bitmap? bitmap = await Task.Run(() =>
            {
                try { using var stream = File.OpenRead(path); return Bitmap.DecodeToWidth(stream, width); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
            }).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (state.Generation != generation) { bitmap?.Dispose(); return; }
                state.Bitmap = bitmap; image.Source = bitmap;
            });
        }
    }
}
