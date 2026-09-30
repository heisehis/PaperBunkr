using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Paperbunkr.App.ViewModels.Home;

namespace Paperbunkr.App.Views.Home;

/// <summary>
/// Picks each Home section's template by its key (docs/superpowers/specs/2026-09-28-home-improvements-design.md, "Section model").
/// The templates are keyed <c>HomeSection.&lt;key&gt;</c> resources, one per file under <c>Views/Home/</c> - Avalonia doesn't match
/// type-implicit DataTemplates out of a merged resource dictionary, so <c>HomeScreen</c> hands this a resource lookup instead.
/// </summary>
public sealed class HomeSectionTemplateSelector : IDataTemplate
{
    private readonly Func<string, IDataTemplate?> _lookup;

    public HomeSectionTemplateSelector(Func<string, IDataTemplate?> lookup) => _lookup = lookup;

    public static string ResourceKeyFor(string sectionKey) => $"HomeSection.{sectionKey}";

    public bool Match(object? data) => data is HomeSectionViewModel;

    public Control? Build(object? param)
        => param is HomeSectionViewModel section && _lookup(ResourceKeyFor(section.Key)) is { } template
            ? template.Build(section)
            : new TextBlock { Text = (param as HomeSectionViewModel)?.Title };
}
