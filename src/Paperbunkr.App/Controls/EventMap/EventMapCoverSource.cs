using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>Viewport-bounded, asynchronously decoded map covers. Shared cache hits are never disposed here.</summary>
internal sealed class EventMapCoverSource : IDisposable
{
    private const int Capacity = 200;
    private readonly bool _synchronous;
    private readonly Dictionary<(string Key, int Width), Bitmap> _images = new();
    private readonly LinkedList<(string Key, int Width)> _lru = new();
    private readonly HashSet<(string Key, int Width)> _pending = new();
    private readonly HashSet<(string Key, int Width)> _missing = new();
    private HashSet<string> _wanted = new();
    private bool _disposed;
    private readonly Action _invalidate;
    private readonly Func<string, Bitmap?> _cached;
    private readonly Func<string, int, Bitmap?> _decode;
    private readonly Action<Action> _post;

    /// <param name="synchronous">Decode on the calling thread and keep everything (no LRU): for an off-screen export, which needs every
    /// cover in its single render. The export caps how many covers it asks for.</param>
    public EventMapCoverSource(Action invalidate, Func<string, Bitmap?>? cached = null,
        Func<string, int, Bitmap?>? decode = null, Action<Action>? post = null, bool synchronous = false)
    {
        _synchronous = synchronous;
        _invalidate = invalidate;
        _cached = cached ?? (key => CoverImageCache.TryGetCached(key, out var bitmap) ? bitmap : null);
        _decode = decode ?? Decode;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
    }

    internal int Count => _images.Count;

    public void SetWanted(IEnumerable<string> keys)
    {
        _wanted = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in _images.Keys.Where(k => !_wanted.Contains(k.Key)).ToArray()) Remove(key);
        _missing.RemoveWhere(k => !_wanted.Contains(k.Key));
    }

    public Bitmap? Get(string? key, int width)
    {
        if (_disposed || string.IsNullOrWhiteSpace(key)) return null;
        if (_cached(key) is { } shared) return shared;
        var entry = (key, width <= 48 ? 48 : 256);
        if (_images.TryGetValue(entry, out var bitmap))
        {
            _lru.Remove(entry);
            _lru.AddLast(entry);
            return bitmap;
        }
        if (_synchronous)
        {
            if (_missing.Contains(entry)) return null;
            var inline = _decode(entry.key, entry.Item2);
            if (inline is null) { _missing.Add(entry); return null; }
            _images[entry] = inline;
            return inline;
        }
        if (_missing.Contains(entry) || !_pending.Add(entry)) return null;
        _ = Task.Run(() => _decode(entry.key, entry.Item2)).ContinueWith(task => _post(() =>
        {
            _pending.Remove(entry);
            var decoded = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            if (_disposed || !_wanted.Contains(entry.key)) { decoded?.Dispose(); return; }
            if (decoded is null) { _missing.Add(entry); return; }
            _images[entry] = decoded;
            _lru.AddLast(entry);
            while (_images.Count > Capacity && _lru.First is { } first) Remove(first.Value);
            _invalidate();
        }));
        return null;
    }

    private static Bitmap? Decode(string key, int width)
    {
        try
        {
            string path = CoverImageCache.ResolveCoverFile(key);
            if (path.Length == 0) return null;
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch { return null; }
    }

    private void Remove((string Key, int Width) key)
    {
        _lru.Remove(key);
        if (_images.Remove(key, out var bitmap)) bitmap.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var bitmap in _images.Values) bitmap.Dispose();
        _images.Clear();
        _lru.Clear();
    }
}
