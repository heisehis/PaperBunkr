namespace Paperbunkr.Plugins;

/// <summary>
/// Thin Avalonia-appropriate stand-in for CE's <c>IWin32Window MainWindow</c> (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §4) - just enough for a plugin to own a modal dialog,
/// not the raw Avalonia <c>Window</c> type.
/// </summary>
public interface IPluginHostWindow
{
    /// <summary>Opaque owner handle a host adapter can cast back to its real Avalonia <c>Window</c> to parent a dialog.</summary>
    object Owner { get; }
}
