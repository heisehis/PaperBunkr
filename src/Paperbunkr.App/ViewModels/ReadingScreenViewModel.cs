using System.Collections.ObjectModel;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reading Lists screen, ported from ReadingScreen.dc.html (Claude Design project 43c40b25),
/// "separate" lists-mode variant (the default in the parent Paperbunkr App wireframe - no
/// Smart/Reading toggle, no visual completion bar).
/// </summary>
public partial class ReadingScreenViewModel : ViewModelBase
{
    public ReadingScreenViewModel()
    {
        Arcs = new ObservableCollection<ReadingArcSample>
        {
            new()
            {
                Title = "Kilo Station Vol. 3",
                Issues = new ObservableCollection<ReadingArcIssueSample>
                {
                    new() { Num = "#11", Name = "The Ring Goes Dark", IsOwned = true },
                    new() { Num = "#12", Name = "Last Broadcast", IsOwned = true },
                    new() { Num = "#13", Name = "Static and Salt", IsOwned = false },
                },
            },
            new()
            {
                Title = "Brass Horizon crossover",
                Issues = new ObservableCollection<ReadingArcIssueSample>
                {
                    new() { Num = "#12", Name = "What the Rivets Knew", IsOwned = true },
                    new() { Num = "#13", Name = "The Consortium Answers", IsOwned = false },
                    new() { Num = "#14", Name = "Signal War, Part One", IsOwned = true },
                },
            },
        };
    }

    public ObservableCollection<ReadingArcSample> Arcs { get; }

    public string ListName => "Kilo Station: Signal War";
    public string Subtitle => "Cross-series reading order · tracked story arc";
    public string TotalIssues => "16";
    public string OwnedIssues => "14";
    public string MissingIssues => "2";
}
