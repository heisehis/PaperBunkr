using Avalonia;
using Avalonia.Controls;
using FluentIcons.Common;

namespace Paperbunkr.App.Views.Preferences;

/// <summary>
/// Shared icon+title+description+control row (docs/superpowers/specs/2026-09-07-preferences-
/// tile-hub-redesign-{design,plan}.md item 2). <see cref="SettingsContent"/> is a distinct property
/// from the inherited <c>ContentControl.Content</c> (unused here - this control's body is plain
/// inline XAML, not a template driven by the base <c>Content</c> slot) so a consumer sets it
/// explicitly (<c>SettingsContent="{Binding ...}"</c>) rather than via child-element shorthand.
/// </summary>
public partial class SettingsRow : UserControl
{
    public static readonly StyledProperty<Symbol> IconProperty =
        AvaloniaProperty.Register<SettingsRow, Symbol>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

    public static readonly StyledProperty<object?> SettingsContentProperty =
        AvaloniaProperty.Register<SettingsRow, object?>(nameof(SettingsContent));

    public Symbol Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? SettingsContent
    {
        get => GetValue(SettingsContentProperty);
        set => SetValue(SettingsContentProperty, value);
    }

    public SettingsRow() => InitializeComponent();
}
