namespace Paperbunkr.Plugins.Tests;

/// <summary>Recording <see cref="IPluginLogger"/> for tests; shared by reference across environment clones and thread-safe (hooks log from background threads).</summary>
internal sealed class FakePluginLogger : IPluginLogger
{
    private readonly object _gate = new();
    private readonly List<(string Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyList<(string Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    private void Add(string level, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            _entries.Add((level, message, exception));
        }
    }

    public void Debug(string message) => Add("Debug", message);

    public void Info(string message) => Add("Info", message);

    public void Warn(string message) => Add("Warn", message);

    public void Error(string message, Exception? exception = null) => Add("Error", message, exception);
}
