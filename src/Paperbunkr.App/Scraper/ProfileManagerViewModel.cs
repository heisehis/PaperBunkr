using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Organizing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;
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

    [ObservableProperty]
    private bool _useForScheduledRun;

    [ObservableProperty]
    private bool _useFolder;

    [ObservableProperty]
    private bool _useFileName;

    [ObservableProperty]
    private string _emptyFolder;

    /// <summary>One <c>token=text</c> per line: what a token that comes out empty is replaced with (the plugin's "empty data").</summary>
    [ObservableProperty]
    private string _emptyDataText;

    [ObservableProperty]
    private bool _failEmptyValues;

    /// <summary>The tokens that must not be empty, separated by commas (<c>series, number</c>); only used while <see cref="FailEmptyValues"/> is on.</summary>
    [ObservableProperty]
    private string _failedFieldsText;

    /// <summary>One folder per line; a book whose path contains any of them is left alone.</summary>
    [ObservableProperty]
    private string _excludeFoldersText;

    /// <summary>The exclude rule: a group of conditions and child groups, any depth. Empty means nothing is excluded.</summary>
    public ExcludeGroupViewModel RootGroup { get; }

    /// <summary>The top-level group's own conditions (the flat view of <see cref="RootGroup"/>).</summary>
    public ObservableCollection<ExcludeConditionRowViewModel> ExcludeConditions => RootGroup.Conditions;

    /// <summary>True: a book matching ANY entry of the top-level group is excluded (the plugin's default); false: it must match ALL of them.</summary>
    public bool ExcludeMatchAny
    {
        get => RootGroup.MatchAny;
        set => RootGroup.MatchAny = value;
    }

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
        _useForScheduledRun = profile.UseForScheduledRun;
        _useFolder = profile.UseFolder;
        _useFileName = profile.UseFileName;
        _emptyFolder = profile.EmptyFolder;
        _emptyDataText = string.Join(Environment.NewLine, profile.EmptyData.Select(e => $"{e.Key}={e.Value}"));
        _failEmptyValues = profile.FailEmptyValues;
        _failedFieldsText = string.Join(", ", profile.FailedFields);
        _excludeFoldersText = string.Join(Environment.NewLine, profile.ExcludeFolders);

        PluginConditionGroup? rule = null;
        if (!string.IsNullOrEmpty(profile.ExcludeRuleJson))
        {
            try
            {
                rule = JsonSerializer.Deserialize<PluginConditionGroup>(profile.ExcludeRuleJson);
            }
            catch (JsonException)
            {
                // A hand-edited or corrupted rule blob - treat it as "no rule" rather than throwing
                // out of a settings-screen constructor; saving this profile again will simply drop it.
            }
        }

        RootGroup = rule is null ? new ExcludeGroupViewModel(matchAny: true) : ExcludeGroupViewModel.From(rule);
    }

    public void AddExcludeCondition() => RootGroup.AddConditionCommand.Execute(null);

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
        UseForScheduledRun = UseForScheduledRun,
        UseFolder = UseFolder,
        UseFileName = UseFileName,
        EmptyFolder = EmptyFolder ?? string.Empty,
        EmptyData = ParseEmptyData(EmptyDataText),
        FailEmptyValues = FailEmptyValues,
        FailedFields = SplitList(FailedFieldsText, ',').ToList(),
        ExcludeFolders = SplitList(ExcludeFoldersText, '\n', '\r').ToList(),
        ExcludeRuleJson = RootGroup.IsEmpty ? null : JsonSerializer.Serialize(RootGroup.ToGroup()),
    };

    private static IEnumerable<string> SplitList(string? text, params char[] separators) =>
        (text ?? string.Empty).Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Dictionary<string, string> ParseEmptyData(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in SplitList(text, '\n', '\r'))
        {
            int equals = line.IndexOf('=');
            if (equals > 0 && line[(equals + 1)..].Trim() is { Length: > 0 } value)
            {
                result[line[..equals].Trim().Trim('<', '>')] = value;
            }
        }

        return result;
    }
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
    private readonly IActivityService? _activity;

    public ObservableCollection<OrganizerProfileRowViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private OrganizerProfileRowViewModel? _selected;

    [ObservableProperty]
    private bool _isUndoing;

    [ObservableProperty]
    private string? _undoStatusMessage;

    /// <summary>Why the last Save was refused (a template problem), or null.</summary>
    [ObservableProperty]
    private string? _saveStatusMessage;

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
        Func<PaperbunkrDbContext>? createDbContext = null,
        IActivityService? activity = null)
    {
        _activity = activity;
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
    public void Reload()
    {
        Refresh();
        _previewSample = null;          // the library may have changed since the last time the editor was open
        SchedulePreview();
    }

    private void Refresh()
    {
        int? previouslySelectedId = Selected?.Id;
        _suspendPreview = true;         // rebuilding the list re-selects a profile; that is not an edit worth a preview run
        try
        {
            Profiles.Clear();
            foreach (OrganizerProfile profile in _store.GetAll())
            {
                Profiles.Add(new OrganizerProfileRowViewModel(profile));
            }

            Selected = Profiles.FirstOrDefault(p => p.Id == previouslySelectedId) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _suspendPreview = false;
        }

        _onProfilesChanged?.Invoke();
    }

    // -- Live preview ---------------------------------------------------------------------------------------------------

    /// <summary>What the templates do to a few real comics, redrawn as the profile is edited. Nothing is moved.</summary>
    public ObservableCollection<PreviewRowViewModel> PreviewRows { get; } = new();

    /// <summary>Why there are no preview rows (no base folder yet, a template problem, an empty library), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    private string? _previewMessage;

    public bool HasPreviewMessage => !string.IsNullOrEmpty(PreviewMessage);

    /// <summary>How long after the last keystroke the preview is recomputed.</summary>
    internal TimeSpan PreviewDelay { get; set; } = TimeSpan.FromMilliseconds(350);

    private static readonly HashSet<string> PreviewInputs = new(StringComparer.Ordinal)
    {
        nameof(OrganizerProfileRowViewModel.FolderTemplate), nameof(OrganizerProfileRowViewModel.FileTemplate),
        nameof(OrganizerProfileRowViewModel.BaseFolder), nameof(OrganizerProfileRowViewModel.UseFolder),
        nameof(OrganizerProfileRowViewModel.UseFileName), nameof(OrganizerProfileRowViewModel.EmptyFolder),
        nameof(OrganizerProfileRowViewModel.EmptyDataText), nameof(OrganizerProfileRowViewModel.FailEmptyValues),
        nameof(OrganizerProfileRowViewModel.FailedFieldsText), nameof(OrganizerProfileRowViewModel.ExcludeFoldersText),
    };

    /// <summary>The most recently scheduled preview run (test seam: lets a test wait for it).</summary>
    internal Task? PreviewTask { get; private set; }

    private bool _suspendPreview;
    private CancellationTokenSource? _previewCts;
    private List<Issue>? _previewSample;

    partial void OnSelectedChanged(OrganizerProfileRowViewModel? oldValue, OrganizerProfileRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedRowPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedRowPropertyChanged;
        }

        if (!_suspendPreview)
        {
            SchedulePreview();
        }
    }

    private void OnSelectedRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && PreviewInputs.Contains(e.PropertyName))
        {
            SchedulePreview();
        }
    }

    /// <summary>Recomputes the preview shortly after the last edit. The profile is snapshotted here, on the calling (UI) thread, so the
    /// background work never touches the editor's collections.</summary>
    private void SchedulePreview()
    {
        if (_organizerService is null || _createDbContext is null || Selected is null)
        {
            return;
        }

        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        var snapshot = Selected.ToProfile();
        var delay = PreviewDelay;
        PreviewTask = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                }

                await ComputePreviewAsync(snapshot, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer edit
            }
            catch (Exception ex)
            {
                ShowPreview(Array.Empty<PreviewRowViewModel>(), $"The preview could not be computed: {ex.Message}");
            }
        });
    }

    /// <summary>The "Refresh" button, and the awaitable entry point for tests.</summary>
    [RelayCommand]
    private async Task RefreshPreview()
    {
        if (Selected is null)
        {
            ShowPreview(Array.Empty<PreviewRowViewModel>(), null);
            return;
        }

        _previewSample = null;
        await ComputePreviewAsync(Selected.ToProfile(), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ComputePreviewAsync(OrganizerProfile profile, CancellationToken cancellationToken)
    {
        if (_organizerService is null || _createDbContext is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.BaseFolder))
        {
            ShowPreview(Array.Empty<PreviewRowViewModel>(), "Set a base folder to see where the files would go.");
            return;
        }

        if (TemplateProblem(profile) is { } problem)
        {
            ShowPreview(Array.Empty<PreviewRowViewModel>(), problem);
            return;
        }

        if (_previewSample is null)
        {
            using var context = _createDbContext();
            _previewSample = OrganizerPreviewSample.Pick(context);
        }

        var sample = _previewSample;
        if (sample.Count == 0)
        {
            ShowPreview(Array.Empty<PreviewRowViewModel>(), "Add comics to your library to see a preview.");
            return;
        }

        var plan = await _organizerService.PreviewAsync(sample, profile, _createDbContext).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ShowPreview(sample.Select(issue => DescribePreview(issue, plan, profile)).ToList(), null);
    }

    private static PreviewRowViewModel DescribePreview(Issue issue, OrganizePlan plan, OrganizerProfile profile)
    {
        var move = plan.Moves.FirstOrDefault(m => m.Issue.Id == issue.Id);
        string label = move is not null ? OrganizePlanSummary.Describe(move) : $"{issue.Series?.Name} #{issue.Number}".Trim();
        if (move is null)
        {
            return new PreviewRowViewModel(label, "Left where it is - it is in an excluded folder.", isProblem: false);
        }

        if (move.Problem is not null)
        {
            return new PreviewRowViewModel(label, move.Problem, isProblem: true);
        }

        if (move.SkipReason is not null)
        {
            return new PreviewRowViewModel(label, $"Left where it is - {move.SkipReason}.", isProblem: false);
        }

        if (move.IsAlreadyInPlace)
        {
            return new PreviewRowViewModel(label, "Already in the right place.", isProblem: false);
        }

        string relative = System.IO.Path.GetRelativePath(profile.BaseFolder, move.DestinationPath);
        return new PreviewRowViewModel(label, "→ " + (relative.StartsWith("..", StringComparison.Ordinal) ? move.DestinationPath : relative), isProblem: false);
    }

    /// <summary>Publishes preview rows on the UI thread (the computation may finish on a worker).</summary>
    private void ShowPreview(IReadOnlyList<PreviewRowViewModel> rows, string? message)
    {
        void Apply()
        {
            PreviewRows.Clear();
            foreach (var row in rows)
            {
                PreviewRows.Add(row);
            }

            PreviewMessage = message;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    /// <summary>The same template checks Save makes, on a profile snapshot (null when the templates are usable).</summary>
    private static string? TemplateProblem(OrganizerProfile profile)
    {
        string? problem = profile.UseFolder && TemplateValidator.Validate(profile.FolderTemplate, allowEmpty: true) is { } folderProblem ? $"Folder template: {folderProblem}" : null;
        return problem ?? (profile.UseFileName && TemplateValidator.Validate(profile.FileTemplate, allowEmpty: false) is { } fileProblem ? $"File template: {fileProblem}" : null);
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

        // A template problem is shown now, not discovered as a failed book on the next run.
        string? problem = TemplateProblem(Selected.ToProfile());
        SaveStatusMessage = problem;
        if (problem is not null)
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
        using var job = _activity?.StartJob(ActivityJobKind.Import, "Undoing the last organize", cancellable: false);
        try
        {
            UndoResult result = await _organizerService.UndoLastOrganizeAsync(_createDbContext);
            UndoStatusMessage = result switch
            {
                { Reversed: 0, Failed: 0 } => "Nothing to undo - no recent organize batch found.",
                { Failed: 0 } => $"Reversed {result.Reversed} file(s).",
                _ => $"Reversed {result.Reversed} file(s); {result.Failed} failed: {string.Join("; ", result.Errors)}",
            };
            job?.Succeed(UndoStatusMessage, itemsProcessed: result.Reversed, itemsFailed: result.Failed);
        }
        catch (Exception ex)
        {
            UndoStatusMessage = $"Undo failed: {ex.Message}";
            job?.Fail(UndoStatusMessage, ex: ex);
        }
        finally
        {
            IsUndoing = false;
        }
    }
}
