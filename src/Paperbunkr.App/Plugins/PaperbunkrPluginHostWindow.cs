using Avalonia.Controls;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>Real adapter for <see cref="IPluginHostWindow"/> - wraps <see cref="MainWindow"/> as an opaque owner handle.</summary>
public sealed class PaperbunkrPluginHostWindow : IPluginHostWindow
{
    public PaperbunkrPluginHostWindow(Window mainWindow) => Owner = mainWindow;

    public object Owner { get; }
}
