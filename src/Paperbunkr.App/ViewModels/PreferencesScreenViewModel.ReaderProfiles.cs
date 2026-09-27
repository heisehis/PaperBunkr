using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences &gt; Reader &gt; PROFILES (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md section 4): the default profile picker and the profile list with rename, delete and
/// reorder. Profiles themselves are captured from the reader's drawer, not edited here. Same workspace-row commands as the Library and Books screens, on the shared
/// <see cref="WorkspaceService"/>.
/// </summary>
public partial class PreferencesScreenViewModel
{
    private const string StandardProfileName = "Standard";

    private WorkspaceService ProfileWorkspaces => new(_contextFactory);

    /// <summary>The shared naming overlay (<c>MainViewModel.PromptWorkspaceName</c>); a no-op until wired.</summary>
    public Action<string?, Action<string>> PromptForName { get; set; } = (_, _) => { };

    /// <summary>Every reader profile, built-ins first.</summary>
    public ObservableCollection<WorkspaceRow> ReaderProfiles { get; } = new();

    /// <summary>"Standard" plus every profile name, for the default-profile picker.</summary>
    public string[] ReaderProfileChoiceNames { get; private set; } = [StandardProfileName];

    private int? _defaultReaderProfileId;

    /// <summary>The default profile's name ("Standard" when there is none). Setting it to a listed name persists it; any other text is ignored (the picker is strict).</summary>
    public string DefaultReaderProfileText
    {
        get => ReaderProfiles.FirstOrDefault(p => p.Id == _defaultReaderProfileId)?.Name ?? StandardProfileName;
        set
        {
            int? id = string.Equals(value, StandardProfileName, StringComparison.Ordinal)
                ? null
                : ReaderProfiles.FirstOrDefault(p => p.Name == value)?.Id;
            if (id is null && !string.Equals(value, StandardProfileName, StringComparison.Ordinal))
            {
                return;
            }

            if (id == _defaultReaderProfileId)
            {
                return;
            }

            _defaultReaderProfileId = id;
            using (var context = _contextFactory())
            {
                context.GetOrCreateAppSettings().DefaultReaderProfileId = id;
                context.SaveChanges();
            }

            OnPropertyChanged();
            ReaderDisplaySettingsChanged?.Invoke();
        }
    }

    /// <summary>Raised after a profile was renamed, deleted or reordered here, so the reader's drawer list (which lives in another view model) reloads.</summary>
    public event Action? ReaderProfilesChanged;

    /// <summary>Reloads the profile list and the default pointer from the database. Also called by the reader when it saves a new profile.</summary>
    public void RefreshReaderProfiles()
    {
        var service = ProfileWorkspaces;
        service.EnsureBuiltInsSeeded();
        ReaderProfiles.Clear();
        foreach (var row in service.List(WorkspaceScreen.Reader))
        {
            ReaderProfiles.Add(new WorkspaceRow(row.Id, row.Name, row.IsBuiltIn, IsActive: false));
        }

        using (var context = _contextFactory())
        {
            int? id = context.GetOrCreateAppSettings().DefaultReaderProfileId;
            _defaultReaderProfileId = ReaderProfiles.Any(p => p.Id == id) ? id : null;
        }

        ReaderProfileChoiceNames = [StandardProfileName, .. ReaderProfiles.Select(p => p.Name)];
        OnPropertyChanged(nameof(ReaderProfileChoiceNames));
        OnPropertyChanged(nameof(DefaultReaderProfileText));
        OnPropertyChanged(nameof(HasReorderableReaderProfiles));
    }

    public bool HasReorderableReaderProfiles => ReaderProfiles.Count(p => !p.IsBuiltIn) > 1;

    [RelayCommand]
    private void RenameReaderProfile(int id)
    {
        var row = ReaderProfiles.FirstOrDefault(p => p.Id == id);
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        PromptForName(row.Name, name =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            ProfileWorkspaces.Rename(id, name.Trim());
            RefreshReaderProfiles();
            ReaderProfilesChanged?.Invoke();
            ReaderDisplaySettingsChanged?.Invoke();
        });
    }

    [RelayCommand]
    private void DeleteReaderProfile(int id)
    {
        var row = ReaderProfiles.FirstOrDefault(p => p.Id == id);
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        ProfileWorkspaces.Delete(id);

        // Deferred: this command runs from the row's own delete Button.Click still routing through the list's ItemsControl - the same detach-during-routing crash the Library and
        // Books workspace rows document (CLAUDE.md).
        Dispatcher.UIThread.Post(() =>
        {
            RefreshReaderProfiles();
            ReaderProfilesChanged?.Invoke();
            ReaderDisplaySettingsChanged?.Invoke();
        });
    }

    [RelayCommand] private void MoveReaderProfileUp(int id) => MoveReaderProfile(id, -1);

    [RelayCommand] private void MoveReaderProfileDown(int id) => MoveReaderProfile(id, +1);

    private void MoveReaderProfile(int id, int delta)
    {
        var user = ReaderProfiles.Where(p => !p.IsBuiltIn).Select(p => p.Id).ToList();
        int index = user.IndexOf(id);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= user.Count)
        {
            return;
        }

        (user[index], user[target]) = (user[target], user[index]);
        ProfileWorkspaces.Reorder(WorkspaceScreen.Reader, user);
        Dispatcher.UIThread.Post(() =>
        {
            RefreshReaderProfiles();
            ReaderProfilesChanged?.Invoke();
        });
    }
}
