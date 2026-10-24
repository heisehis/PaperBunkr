using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Paperbunkr.App.Views.Home;

/// <summary>Home's section list as a reusable part - see the comment in <c>HomeSections.axaml</c>.</summary>
public partial class HomeSections : UserControl
{
    public HomeSections()
    {
        InitializeComponent();
        SectionsHost.ItemTemplate = new HomeSectionTemplateSelector(Lookup);
    }

    /// <summary>The section template registered under a resource key, from this control's own merged template files.</summary>
    internal IDataTemplate? Lookup(string resourceKey) => this.TryFindResource(resourceKey, out var value) ? value as IDataTemplate : null;
}
