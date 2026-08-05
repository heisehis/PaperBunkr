namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Smart Lists screen, ported from SmartScreen.dc.html (Claude Design project 43c40b25),
/// "separate" lists-mode variant (the default in the parent Paperbunkr App wireframe - no
/// Smart/Reading toggle, no "live" visual styling). The rule builder's 3 conditions are
/// static sample content in the source file too (no props), so they're written directly
/// in SmartScreen.axaml rather than bound here - same treatment as DetailMeta/DetailPills.
/// </summary>
public partial class SmartScreenViewModel : ViewModelBase
{
    public string ListName => "Unread Salvage Noir";
    public string Subtitle => "Custom smart list · updates live as your library changes";
    public string MatchCountLabel => "342";
}
