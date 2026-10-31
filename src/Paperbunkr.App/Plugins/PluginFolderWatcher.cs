using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Dev-mode reload trigger (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A1): watches the plugins
/// root for changes to <c>*.csx</c>, <c>*.py</c> and <c>plugin.xml</c>, waits for a quiet period so an editor's save burst (write,
/// rename, write) becomes one reload, then calls <see cref="Changed"/> once with every path that changed. The callback runs on a
/// thread-pool thread; the host marshals it. Never throws out of a watcher event.
/// </summary>
public sealed class PluginFolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _quietPeriod;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public PluginFolderWatcher(string root, TimeSpan quietPeriod, Action<IReadOnlyList<string>> changed)
    {
        Directory.CreateDirectory(root);
        _quietPeriod = quietPeriod;
        Changed = changed;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.Size,
        };
        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += (s, e) =>
        {
            OnEvent(s, e);
            Note(e.OldFullPath);
        };
        _watcher.EnableRaisingEvents = true;
    }

    public Action<IReadOnlyList<string>> Changed { get; }

    /// <summary>True for the files whose change means the plugin should be re-discovered.</summary>
    public static bool IsRelevant(string path)
    {
        string name = Path.GetFileName(path);
        return string.Equals(name, "plugin.xml", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => Note(e.FullPath);

    private void Note(string path)
    {
        if (!IsRelevant(path))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending.Add(path);
            _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        List<string> batch;
        lock (_gate)
        {
            if (_disposed || _pending.Count == 0)
            {
                return;
            }

            batch = _pending.ToList();
            _pending.Clear();
        }

        try
        {
            Changed(batch);
        }
        catch (Exception)
        {
            // A failed reload is the host's to report; it must never take the watcher thread down.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending.Clear();
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
