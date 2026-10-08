using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>One kind tab (Issues / Series / Novels) of the gallery.</summary>
public partial class SmartTemplateKindTab : ObservableObject
{
    public SmartTemplateKindTab(SmartListTargetKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public SmartListTargetKind Kind { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One card of the gallery: the "Blank list" card (<see cref="Template"/> is null) or a template with its rules shown as chips.</summary>
public partial class SmartTemplateCard : ObservableObject
{
    public SmartTemplateCard(SmartListTemplate? template, string name, string description, IReadOnlyList<string> chips)
    {
        Template = template;
        Name = name;
        Description = description;
        Chips = chips;
    }

    public SmartListTemplate? Template { get; }

    public bool IsBlank => Template is null;

    public string Name { get; }

    public string Description { get; }

    /// <summary>The template's rules in words, built from the rules themselves so they cannot drift from what the list will contain.</summary>
    public IReadOnlyList<string> Chips { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The "New Smart List" gallery (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.2): kind tabs, a card grid with
/// "Blank list" first, and a Create button. Creating hands the chosen template (or null for blank) and kind back to the Smart screen,
/// which makes an ordinary editable list. A deliberate deviation from CE, which has no template picker.
/// </summary>
public partial class SmartTemplateGalleryViewModel : ObservableObject
{
    private readonly Action<SmartListTargetKind, SmartListTemplate?> _create;

    public SmartTemplateGalleryViewModel(Action<SmartListTargetKind, SmartListTemplate?> create)
    {
        _create = create;
        Kinds =
        [
            new SmartTemplateKindTab(SmartListTargetKind.Issue, "Issues"),
            new SmartTemplateKindTab(SmartListTargetKind.Series, "Series"),
            new SmartTemplateKindTab(SmartListTargetKind.Novel, "Novels"),
        ];
    }

    public IReadOnlyList<SmartTemplateKindTab> Kinds { get; }

    public ObservableCollection<SmartTemplateCard> Cards { get; } = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private SmartListTargetKind _kind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateLabel))]
    private SmartTemplateCard? _selectedCard;

    public string CreateLabel => SelectedCard is { IsBlank: false } card ? $"Create from \"{card.Name}\"" : "Create blank list";

    /// <summary>Opens the gallery on <paramref name="kind"/>'s tab with "Blank list" selected.</summary>
    public void Open(SmartListTargetKind kind)
    {
        ShowKind(kind);
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void SelectKind(SmartTemplateKindTab? tab)
    {
        if (tab is not null)
        {
            ShowKind(tab.Kind);
        }
    }

    [RelayCommand]
    private void SelectCard(SmartTemplateCard? card)
    {
        if (card is null)
        {
            return;
        }

        foreach (var other in Cards)
        {
            other.IsSelected = ReferenceEquals(other, card);
        }

        SelectedCard = card;
    }

    /// <summary>Creates the selected card's list and closes. The Smart screen reloads its sidebar, so callers run this once the click has finished routing.</summary>
    [RelayCommand]
    private void Create()
    {
        var template = SelectedCard?.Template;
        IsOpen = false;
        _create(Kind, template);
    }

    private void ShowKind(SmartListTargetKind kind)
    {
        Kind = kind;
        foreach (var tab in Kinds)
        {
            tab.IsActive = tab.Kind == kind;
        }

        Cards.Clear();
        Cards.Add(new SmartTemplateCard(null, "Blank list", "Start with no rules and add your own.", []));
        foreach (var template in SmartListTemplateCatalog.For(kind))
        {
            Cards.Add(new SmartTemplateCard(template, template.Name, template.Description, template.Rules.Select(r => Describe(template, r)).ToList()));
        }

        SelectCard(Cards[0]);
    }

    /// <summary>A rule in words: "Unread Issues is greater than 2", "Added not within last (days) 90", "Has Pending Proposal".</summary>
    internal static string Describe(SmartListTemplate template, SmartListTemplateRule rule)
    {
        var definition = SmartListTemplateCatalog.DefinitionsFor(template.Kind)[rule.Field];
        if (definition.DataType == SmartListDataType.Toggle)
        {
            bool wanted = bool.TryParse(rule.Value, out bool b) && b;
            bool positive = wanted == (rule.Operator == SmartListOperator.Is) != rule.Not;
            return positive ? definition.Label : $"Not {definition.Label}";
        }

        string op = SmartListOperatorLabels.Labels.TryGetValue(rule.Operator, out var label) ? label : rule.Operator.ToString();
        string value = rule.Operator is SmartListOperator.InRange or SmartListOperator.DateInRange
            ? $"{rule.Value}–{rule.Value2}"
            : rule.Value.Length == 0 ? "empty" : rule.Value;
        return $"{definition.Label} {(rule.Not ? "not " : string.Empty)}{op} {value}";
    }
}
