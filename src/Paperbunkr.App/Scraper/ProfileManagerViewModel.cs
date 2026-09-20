using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Text.Json;
using Paperbunkr.Data.Organizing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Scraper;

/// <summary>One insertable-token button pair, carrying the actual command references so the XAML
/// DataTemplate never needs to reach back up to the owning <see cref="ProfileManagerViewModel"/>
/// across a DataContext boundary - both commands are the same shared instances for every row, only
/// <see cref="Token"/> (used as each button's own <c>CommandParameter</c>) differs per row.</summary>
public sealed record InsertableToken(string Token, IRelayCommand<string> InsertIntoFolder, IRelayCommand<string> InsertIntoFile);

/// <summary>One exclude condition (design doc §5 - "reuse Paperbunkr's own `IRulesEngine`" - the
/// engine already existed and <see cref="OrganizerProfile.ExcludeRuleJson"/> already consumed it, but
/// no UI ever let a user build one). ANDed together with every other row on the same profile via
/// <see cref="PluginConditionGroup.And"/> - a flat AND list, not the full nested AND/OR tree the
/// engine itself supports, since that's the common case and keeps this editor simple.</summary>
public sealed partial class ExcludeConditionRowViewModel : ObservableObject
{
    private readonly Action<ExcludeConditionRowViewModel> _onRemove;

    /// <summary>Bound via <c>{x:Static}</c> from the DataTemplate - every field/operator the shared
    /// Smart List engine understands (design doc §5). Not filtered by data type here - the engine
    /// itself tolerates an operator/field mismatch the same way a hand-built <c>PluginCondition</c> in
    /// a `.csx` plugin would (see <c>FranchiseTools/rated-not-checked.csx</c>), so this stays a plain
    /// flat list rather than a smarter per-field operator subset.</summary>
    public static IReadOnlyList<SmartListField> AllFields { get; } = Enum.GetValues<SmartListField>();

    public static IReadOnlyList<SmartListOperator> AllOperators { get; } = Enum.GetValues<SmartListOperator>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldText))]
    private SmartListField _field;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatorText))]
    private SmartListOperator _op;

    public static IReadOnlyList<string> FieldNames { get; } = Enum.GetNames<SmartListField>();

    public static IReadOnlyList<string> OperatorNames { get; } = Enum.GetNames<SmartListOperator>();

    /// <summary>The field as text for the suggest box (the app has no ComboBox; see <c>SuggestBox</c>). An unknown name leaves the field unchanged.</summary>
    public string FieldText
    {
        get => Field.ToString();
        set
        {
            if (Enum.TryParse<SmartListField>(value, ignoreCase: true, out var parsed))
            {
                Field = parsed;
            }
        }
    }

    public string OperatorText
    {
        get => Op.ToString();
        set
        {
            if (Enum.TryParse<SmartListOperator>(value, ignoreCase: true, out var parsed))
            {
                Op = parsed;
            }
        }
    }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _not;

    public ExcludeConditionRowViewModel(SmartListField field, SmartListOperator op, string value, bool not, Action<ExcludeConditionRowViewModel> onRemove)
    {
        _field = field;
        _op = op;
        _value = value;
        _not = not;
        _onRemove = onRemove;
    }

    public PluginCondition ToCondition() => new(Field, Op, Value ?? string.Empty, Not: Not);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}

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
    [NotifyPropertyChangedFor(nameof(ModeText))]
    private OrganizerMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollisionPolicyText))]
    private AutomationCollisionPolicy _automationCollisionPolicy;

    public static IReadOnlyList<string> ModeNames { get; } = Enum.GetNames<OrganizerMode>();

    public static IReadOnlyList<string> CollisionPolicyNames { get; } = Enum.GetNames<AutomationCollisionPolicy>();

    public string ModeText
    {
        get => Mode.ToString();
        set
        {
            if (Enum.TryParse<OrganizerMode>(value, ignoreCase: true, out var parsed))
            {
                Mode = parsed;
            }
        }
    }

    public string CollisionPolicyText
    {
        get => AutomationCollisionPolicy.ToString();
        set
        {
            if (Enum.TryParse<AutomationCollisionPolicy>(value, ignoreCase: true, out var parsed))
            {
                AutomationCollisionPolicy = parsed;
            }
        }
    }

    [ObservableProperty]
    private bool _removeEmptyFolders;

    public ObservableCollection<ExcludeConditionRowViewModel> ExcludeConditions { get; } = new();

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

        if (!string.IsNullOrEmpty(profile.ExcludeRuleJson))
        {
            PluginConditionGroup? rule = null;
            try
            {
                rule = JsonSerializer.Deserialize<PluginConditionGroup>(profile.ExcludeRuleJson);
            }
            catch (JsonException)
            {
                // A hand-edited or corrupted rule blob - treat it as "no rule" rather than throwing
                // out of a settings-screen constructor; saving this profile again will simply drop it.
            }

            if (rule is not null)
            {
                foreach (PluginCondition condition in rule.Conditions)
                {
                    ExcludeConditions.Add(new ExcludeConditionRowViewModel(condition.Field, condition.Op, condition.Value, condition.Not, RemoveExcludeCondition));
                }
            }
        }
    }

    public void AddExcludeCondition() =>
        ExcludeConditions.Add(new ExcludeConditionRowViewModel(SmartListField.SeriesName, SmartListOperator.Contains, string.Empty, false, RemoveExcludeCondition));

    private void RemoveExcludeCondition(ExcludeConditionRowViewModel row) => ExcludeConditions.Remove(row);

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
        ExcludeRuleJson = ExcludeConditions.Count == 0
            ? null
            : JsonSerializer.Serialize(PluginConditionGroup.And(ExcludeConditions.Select(c => c.ToCondition()).ToArray())),
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
    private readonly Action? _onProfilesChanged;
    private readonly LibraryOrganizerService? _organizerService;
    private readonly Func<PaperbunkrDbContext>? _createDbContext;

    public ObservableCollection<OrganizerProfileRowViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private OrganizerProfileRowViewModel? _selected;

    [ObservableProperty]
    private bool _isUndoing;

    [ObservableProperty]
    private string? _undoStatusMessage;

    /// <summary>A representative, common subset of the full token grammar (design doc §5) - not
    /// every one of the ~50 supported tokens, since most authors reach for the same handful. The
    /// text box itself accepts any valid token typed by hand; this is just a convenience picker.</summary>
    public IReadOnlyList<OrganizerMode> AvailableModes { get; } = Enum.GetValues<OrganizerMode>();

    public IReadOnlyList<AutomationCollisionPolicy> AvailableCollisionPolicies { get; } = Enum.GetValues<AutomationCollisionPolicy>();

    public IReadOnlyList<InsertableToken> InsertableTokens { get; }

    /// <summary><see cref="organizerService"/>/<paramref name="createDbContext"/> are optional purely
    /// for the existing test constructions of this view model that predate the Undo action and don't
    /// exercise it - the real <c>OrganizerScraperPlugin.CreateSettingsView</c> call site always
    /// supplies both.</summary>
    public ProfileManagerViewModel(
        OrganizerProfileStore store,
        Action? onProfilesChanged = null,
        LibraryOrganizerService? organizerService = null,
        Func<PaperbunkrDbContext>? createDbContext = null)
    {
        _store = store;
        _onProfilesChanged = onProfilesChanged;
        _organizerService = organizerService;
        _createDbContext = createDbContext;

        string[] tokens =
        [
            "{<series>}", "{ Vol.<volume>}", "{ #<number2>}", "{ (of <count2>)}", "{ ({<month>, }<year>)}",
            "{<publisher>}", "{<imprint>}", "{<format>}", "{<genre(, )>}", "{<tags(, )>}",
        ];
        InsertableTokens = tokens.Select(t => new InsertableToken(t, InsertFolderTokenCommand, InsertFileTokenCommand)).ToList();

        Refresh();
    }

    /// <summary>Re-reads the profiles from storage (the section calls this each time it is shown).</summary>
    public void Reload() => Refresh();

    private void Refresh()
    {
        int? previouslySelectedId = Selected?.Id;
        Profiles.Clear();
        foreach (OrganizerProfile profile in _store.GetAll())
        {
            Profiles.Add(new OrganizerProfileRowViewModel(profile));
        }

        Selected = Profiles.FirstOrDefault(p => p.Id == previouslySelectedId) ?? Profiles.FirstOrDefault();
        _onProfilesChanged?.Invoke();
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
    private void AddExcludeCondition() => Selected?.AddExcludeCondition();

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

    /// <summary>Design doc §8's "Undo last organize" action, finally wired to a real button - see
    /// <see cref="LibraryOrganizerService.UndoLastOrganizeAsync"/>'s own doc comment for why this
    /// existed only as an unreachable data structure before.</summary>
    [RelayCommand]
    private async Task UndoLastOrganize()
    {
        if (_organizerService is null || _createDbContext is null || IsUndoing)
        {
            return;
        }

        IsUndoing = true;
        UndoStatusMessage = null;
        try
        {
            UndoResult result = await _organizerService.UndoLastOrganizeAsync(_createDbContext);
            UndoStatusMessage = result switch
            {
                { Reversed: 0, Failed: 0 } => "Nothing to undo - no recent organize batch found.",
                { Failed: 0 } => $"Reversed {result.Reversed} file(s).",
                _ => $"Reversed {result.Reversed} file(s); {result.Failed} failed: {string.Join("; ", result.Errors)}",
            };
        }
        finally
        {
            IsUndoing = false;
        }
    }
}
