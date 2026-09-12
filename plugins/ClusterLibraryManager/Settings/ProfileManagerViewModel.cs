using System.Collections.ObjectModel;
using ClusterLibraryManager.Organizing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClusterLibraryManager.Settings;

/// <summary>One insertable-token button pair, carrying the actual command references so the XAML
/// DataTemplate never needs to reach back up to the owning <see cref="ProfileManagerViewModel"/>
/// across a DataContext boundary - both commands are the same shared instances for every row, only
/// <see cref="Token"/> (used as each button's own <c>CommandParameter</c>) differs per row.</summary>
public sealed record InsertableToken(string Token, IRelayCommand<string> InsertIntoFolder, IRelayCommand<string> InsertIntoFile);

/// <summary>One <see cref="OrganizerProfile"/> being edited - a live-editable copy, saved back
/// explicitly via <see cref="ProfileManagerViewModel.SaveSelectedCommand"/>, not on every keystroke.</summary>
public sealed partial class OrganizerProfileRowViewModel : ObservableObject
{
    public int Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _folderTemplate;

    [ObservableProperty]
    private string _fileTemplate;

    [ObservableProperty]
    private string _baseFolder;

    [ObservableProperty]
    private OrganizerMode _mode;

    [ObservableProperty]
    private AutomationCollisionPolicy _automationCollisionPolicy;

    [ObservableProperty]
    private bool _removeEmptyFolders;

    public OrganizerProfileRowViewModel(OrganizerProfile profile)
    {
        Id = profile.Id;
        _name = profile.Name;
        _folderTemplate = profile.FolderTemplate;
        _fileTemplate = profile.FileTemplate;
        _baseFolder = profile.BaseFolder;
        _mode = profile.Mode;
        _automationCollisionPolicy = profile.AutomationCollisionPolicy;
        _removeEmptyFolders = profile.RemoveEmptyFolders;
    }

    public OrganizerProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name,
        FolderTemplate = FolderTemplate,
        FileTemplate = FileTemplate,
        BaseFolder = BaseFolder,
        Mode = Mode,
        AutomationCollisionPolicy = AutomationCollisionPolicy,
        RemoveEmptyFolders = RemoveEmptyFolders,
    };
}

/// <summary>
/// CRUD for named organizer profiles (design doc §7, grilling Q16=B) - its own view within the
/// plugin's compiled settings UI, not a declarative schema the host renders. Includes the "insert
/// template token" affordance from the original ask, operating on whichever template field
/// (<see cref="InsertFolderTokenCommand"/> vs. <see cref="InsertFileTokenCommand"/>) the user is
/// currently editing.
/// </summary>
public sealed partial class ProfileManagerViewModel : ObservableObject
{
    private readonly OrganizerProfileStore _store;

    public ObservableCollection<OrganizerProfileRowViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private OrganizerProfileRowViewModel? _selected;

    /// <summary>A representative, common subset of the full token grammar (design doc §5) - not
    /// every one of the ~50 supported tokens, since most authors reach for the same handful. The
    /// text box itself accepts any valid token typed by hand; this is just a convenience picker.</summary>
    public IReadOnlyList<OrganizerMode> AvailableModes { get; } = Enum.GetValues<OrganizerMode>();

    public IReadOnlyList<AutomationCollisionPolicy> AvailableCollisionPolicies { get; } = Enum.GetValues<AutomationCollisionPolicy>();

    public IReadOnlyList<InsertableToken> InsertableTokens { get; }

    public ProfileManagerViewModel(OrganizerProfileStore store)
    {
        _store = store;

        string[] tokens =
        [
            "{<series>}", "{ Vol.<volume>}", "{ #<number2>}", "{ (of <count2>)}", "{ ({<month>, }<year>)}",
            "{<publisher>}", "{<imprint>}", "{<format>}", "{<genre(, )>}", "{<tags(, )>}",
        ];
        InsertableTokens = tokens.Select(t => new InsertableToken(t, InsertFolderTokenCommand, InsertFileTokenCommand)).ToList();

        Refresh();
    }

    private void Refresh()
    {
        int? previouslySelectedId = Selected?.Id;
        Profiles.Clear();
        foreach (OrganizerProfile profile in _store.GetAll())
        {
            Profiles.Add(new OrganizerProfileRowViewModel(profile));
        }

        Selected = Profiles.FirstOrDefault(p => p.Id == previouslySelectedId) ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void AddProfile()
    {
        OrganizerProfile saved = _store.Save(new OrganizerProfile { Name = "New Profile" });
        Refresh();
        Selected = Profiles.FirstOrDefault(p => p.Id == saved.Id);
    }

    [RelayCommand]
    private void SaveSelected()
    {
        if (Selected is null)
        {
            return;
        }

        _store.Save(Selected.ToProfile());
        Refresh();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is null)
        {
            return;
        }

        _store.Delete(Selected.Id);
        Refresh();
    }

    [RelayCommand]
    private void InsertFolderToken(string token)
    {
        if (Selected is not null)
        {
            Selected.FolderTemplate += token;
        }
    }

    [RelayCommand]
    private void InsertFileToken(string token)
    {
        if (Selected is not null)
        {
            Selected.FileTemplate += token;
        }
    }
}
