using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views;

public partial class DetailBand : UserControl
{
    /// <summary>
    /// Optional content injected at the start of the inline meta row - the host detail screen
    /// passes its editable content-type <c>ComboBox</c> here (comic/manga); book leaves it null.
    /// </summary>
    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<DetailBand, object?>(nameof(LeadingContent));

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public DetailBand() => InitializeComponent();
}
