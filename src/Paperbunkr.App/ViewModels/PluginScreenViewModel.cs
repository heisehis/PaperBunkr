using System.Collections.ObjectModel;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Plugin screen (Duplicate Finder example), ported from PluginScreen.dc.html (Claude Design
/// project 43c40b25) - the "plugins get a full first-class window, not a settings tab" case
/// from docs/wireframe_prototype_prompt.md.
/// </summary>
public partial class PluginScreenViewModel : ViewModelBase
{
    public PluginScreenViewModel()
    {
        Groups = new ObservableCollection<DuplicateGroupSample>
        {
            new()
            {
                Title = "Brass Horizon #12",
                Note = "2 copies, keep one",
                Items = new ObservableCollection<DuplicateItemSample>
                {
                    new() { FileName = "brass_horizon_012_v2_scan.cbz", Info = "1.4 GB · 2480×3508 · added 4 months ago", Keep = true },
                    new() { FileName = "Brass Horizon 012 (webrip).cbz", Info = "210 MB · 1600×2263 · added 2 weeks ago", Keep = false },
                },
            },
            new()
            {
                Title = "Nightshift Orchid Vol. 4",
                Note = "2 copies, keep one",
                Items = new ObservableCollection<DuplicateItemSample>
                {
                    new() { FileName = "Nightshift_Orchid_v04.cbz", Info = "89 MB · added 1 year ago", Keep = false },
                    new() { FileName = "Nightshift Orchid v4 [Digital].cbz", Info = "312 MB · higher resolution · added 3 months ago", Keep = true },
                },
            },
        };
    }

    public ObservableCollection<DuplicateGroupSample> Groups { get; }

    public string PluginBadge => "Plugin · Duplicate Finder v1.4";
    public string LastScanLabel => "Last scan: 11 minutes ago · 1,847 series scanned";
    public string GroupCount => "7";
}
