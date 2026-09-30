using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// One issue card on the Event Map (docs/superpowers/specs/2026-09-25-event-map-design.md §3). A real, focusable
/// control - that's why the map realizes cards instead of drawing them (approach B): each gets its own UIA peer named
/// "{Series} #{Number}, {role}, {read state}". <see cref="EventMapSurface"/> recycles cards by swapping
/// <see cref="StyledElement.DataContext"/>; this class mirrors the view model's selection/dimming/missing state into
/// the <c>:selected</c>, <c>:dimmed</c>, <c>:missing</c> and <c>:density-*</c> pseudoclasses the card themes style.
/// </summary>
public class EventMapCard : TemplatedControl
{
    public static readonly StyledProperty<EventMapDensity> DensityProperty =
        AvaloniaProperty.Register<EventMapCard, EventMapDensity>(nameof(Density), EventMapDensity.Standard);

    private EventMapCardViewModel? _card;

    static EventMapCard()
    {
        FocusableProperty.OverrideDefaultValue<EventMapCard>(true);
    }

    public EventMapCard()
    {
        UpdateDensityClasses();
    }

    public EventMapDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    /// <summary>The cell index of the bound card, or -1 while pooled.</summary>
    public int CellIndex => _card?.Index ?? -1;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DensityProperty)
        {
            UpdateDensityClasses();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_card is not null)
        {
            _card.PropertyChanged -= OnCardPropertyChanged;
        }

        _card = DataContext as EventMapCardViewModel;
        if (_card is not null)
        {
            _card.PropertyChanged += OnCardPropertyChanged;
        }

        UpdateState();
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateState();

    private void UpdateDensityClasses()
    {
        PseudoClasses.Set(":density-compact", Density == EventMapDensity.Compact);
        PseudoClasses.Set(":density-standard", Density == EventMapDensity.Standard);
        PseudoClasses.Set(":density-covers", Density == EventMapDensity.Covers);
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":selected", _card?.IsSelected == true);
        PseudoClasses.Set(":dimmed", _card?.IsDimmed == true);
        PseudoClasses.Set(":missing", _card?.IsMissing == true);
        PseudoClasses.Set(":trunk", _card?.IsTrunk == true);
        AutomationProperties.SetName(this, _card?.AutomationName);
        ToolTip.SetTip(this, _card?.ToolTipText);
    }
}
