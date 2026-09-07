using Avalonia;
using Avalonia.Controls.Primitives;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Icon-driven content-status badge (docs/superpowers/specs/2026-09-06-feedback-notification-
/// system-design.md §4), replacing 6+ divergent read/in-progress/new treatments across
/// DetailTabs (3 tile templates), BookDetailScreen, and MangaDetailScreen's "NEW" chapter pill.
/// <see cref="StatusBadgeVariant.InProgress"/> converges on one icon treatment everywhere - the
/// old poster-tile-only accent-bar treatment is dropped, since it didn't generalize to list/card
/// rows anyway.
///
/// Code-only <see cref="TemplatedControl"/> (the <c>BrandMark</c>/<c>SplitText</c> pattern -
/// template is an implicit <see cref="Avalonia.Controls.ControlTheme"/> in
/// <c>Styles/Badges.axaml</c>). <see cref="Variant"/> is the only settable input; the three
/// <c>Is*</c> outputs are computed so the template can switch on them with a plain
/// <c>TemplateBinding</c> instead of a converter, same shape as <c>BrandMark</c>'s
/// <c>IsImage</c>/<c>IsGlyph</c>/<c>IsChip</c>.
/// </summary>
public class StatusBadge : TemplatedControl
{
    public static readonly StyledProperty<StatusBadgeVariant> VariantProperty =
        AvaloniaProperty.Register<StatusBadge, StatusBadgeVariant>(nameof(Variant));

    /// <summary>Icon size for Read/InProgress variants - callers used different sizes at different
    /// call sites before this control existed (poster tile: PbIconSizeMd, list row: PbIconSizeXs),
    /// same as BrandMark's MarkSize.</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StatusBadge, double>(nameof(IconSize), defaultValue: 16d);

    public static readonly DirectProperty<StatusBadge, bool> IsReadProperty =
        AvaloniaProperty.RegisterDirect<StatusBadge, bool>(nameof(IsRead), o => o._isRead);

    public static readonly DirectProperty<StatusBadge, bool> IsInProgressProperty =
        AvaloniaProperty.RegisterDirect<StatusBadge, bool>(nameof(IsInProgress), o => o._isInProgress);

    public static readonly DirectProperty<StatusBadge, bool> IsNewProperty =
        AvaloniaProperty.RegisterDirect<StatusBadge, bool>(nameof(IsNew), o => o._isNew);

    private bool _isRead;
    private bool _isInProgress;
    private bool _isNew;

    public StatusBadge()
    {
        Rebuild();
    }

    public StatusBadgeVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public bool IsRead => _isRead;
    public bool IsInProgress => _isInProgress;
    public bool IsNew => _isNew;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        SetAndRaise(IsReadProperty, ref _isRead, Variant == StatusBadgeVariant.Read);
        SetAndRaise(IsInProgressProperty, ref _isInProgress, Variant == StatusBadgeVariant.InProgress);
        SetAndRaise(IsNewProperty, ref _isNew, Variant == StatusBadgeVariant.New);
    }
}
