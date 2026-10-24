using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A theme-background veil over a hero's blurred cover, only on a light theme. The hero text is the theme's text colour - dark on Daylight -
/// and a blurred cover is dark however light the theme is, so without this the title, tags and synopsis sit dark-on-dark. On a dark theme it is
/// invisible (opacity 0), so those heroes look as they always did. Put it between the backdrop and the fade gradient.
/// </summary>
public sealed class HeroWash : Border
{
    public static readonly StyledProperty<double> LightOpacityProperty =
        AvaloniaProperty.Register<HeroWash, double>(nameof(LightOpacity), 0.7);

    public HeroWash()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>How opaque the veil is on a light theme.</summary>
    public double LightOpacity
    {
        get => GetValue(LightOpacityProperty);
        set => SetValue(LightOpacityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LightOpacityProperty)
        {
            Apply();
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Apply();
    }

    private void Apply() => Opacity = ActualThemeVariant == ThemeVariant.Light ? LightOpacity : 0;
}
