using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A card part that only exists while it is needed (docs/superpowers/specs/2026-09-19-library-scroll-
/// smoothness-design.md §4). Avalonia has no <c>x:Load</c>: a control inside an <c>IsVisible="False"</c>
/// parent is still created, styled, templated and bound. A Library poster card carries a dozen such
/// parts (selection checkbox, publisher chip, dog-ear and plugin-overlay images, rating badge), invisible
/// for most cards at most times, and the harness priced them at ~85 % of a card's realization cost.
///
/// <see cref="ContentTemplate"/> is built only while <see cref="IsActive"/> is <c>true</c> and dropped
/// again when it turns <c>false</c>. The built content is this decorator's <see cref="Decorator.Child"/>,
/// so it inherits the card's <c>DataContext</c> (the item), sits in the same layout slot the eager element
/// used to (the decorator stretches into its parent, so the child's own alignment/margin behave as before),
/// and needs no <c>ContentPresenter</c>. When a virtualizing panel recycles the card to another item the
/// content simply rebinds through the inherited <c>DataContext</c>; it is rebuilt only if
/// <see cref="IsActive"/> actually changes.
///
/// Bind <see cref="IsActive"/> from the <b>card's own</b> name scope (a sibling <c>x:Name</c>, or the view
/// model through <c>$parent[UserControl]</c>): names declared inside <see cref="ContentTemplate"/> live in the
/// nested template's own scope and cannot be reached from outside it.
/// </summary>
public sealed class LazyPart : Decorator
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LazyPart, bool>(nameof(IsActive));

    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<LazyPart, IDataTemplate?>(nameof(ContentTemplate));

    static LazyPart()
    {
        IsActiveProperty.Changed.AddClassHandler<LazyPart>((part, _) => part.Sync());
        ContentTemplateProperty.Changed.AddClassHandler<LazyPart>((part, _) =>
        {
            // A different template replaces whatever the old one built.
            part.Child = null;
            part.Sync();
        });
    }

    public LazyPart()
    {
        // An empty part must not take part in layout at all: a StackPanel adds its Spacing between every *visible*
        // child, and an empty decorator is still "visible" with size 0 (a hidden eager element was not).
        SetCurrentValue(IsVisibleProperty, false);
    }

    /// <summary>True while the part should exist.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>What to build while active. Built with the item as the template parameter; the result inherits this decorator's <c>DataContext</c>.</summary>
    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    private void Sync()
    {
        if (!IsActive)
        {
            Child = null;
            SetCurrentValue(IsVisibleProperty, false);
            return;
        }

        if (Child is null && ContentTemplate is { } template)
        {
            Child = template.Build(DataContext);
        }

        SetCurrentValue(IsVisibleProperty, Child is not null);
    }
}
