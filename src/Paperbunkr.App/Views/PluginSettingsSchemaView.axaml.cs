using Avalonia.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Settings overlay for a plugin that declares a <c>&lt;Settings&gt;</c> schema (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §6.4). Deliberately no logic here: see
/// <see cref="ViewModels.PluginSettingsSchemaViewModel"/>.
/// </summary>
public partial class PluginSettingsSchemaView : UserControl
{
    public PluginSettingsSchemaView() => InitializeComponent();
}
