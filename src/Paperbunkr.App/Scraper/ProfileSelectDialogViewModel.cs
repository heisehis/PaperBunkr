using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Scraper;

/// <summary>One profile in the picker, with a tick.</summary>
public sealed partial class ProfileSelectRowViewModel : ObservableObject
{
    public OrganizerProfile Profile { get; }

    public string Name => Profile.Name;

    /// <summary>What the profile does, in one line - "Move to D:/Comics", "Copy to E:/Backup".</summary>
    public string Description => $"{Profile.Mode} - {(string.IsNullOrWhiteSpace(Profile.BaseFolder) ? "no base folder set" : Profile.BaseFolder)}";

    [ObservableProperty]
    private bool _isSelected;

    public ProfileSelectRowViewModel(OrganizerProfile profile, bool isSelected)
    {
        Profile = profile;
        _isSelected = isSelected;
    }
}

/// <summary>
/// Backs <see cref="ProfileSelectDialogView"/> - shown before a manual "Organize" when more than one profile exists. Tick one or several: a
/// run with several profiles works the way the Library Organizer plugin's does - every Copy profile copies each book, and of the Move
/// profiles only the last one that can place a book moves it (earlier ones report it as moved by a later profile).
/// </summary>
public sealed partial class ProfileSelectDialogViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<OrganizerProfile>?> _resolve;

    public IReadOnlyList<ProfileSelectRowViewModel> Profiles { get; }

    public ProfileSelectDialogViewModel(IReadOnlyList<OrganizerProfile> profiles, Action<IReadOnlyList<OrganizerProfile>?> resolve)
    {
        _resolve = resolve;
        Profiles = profiles.Select((p, i) => new ProfileSelectRowViewModel(p, isSelected: i == 0)).ToList();
        foreach (var row in Profiles)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProfileSelectRowViewModel.IsSelected))
                {
                    RunCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(Summary));
                }
            };
        }
    }

    public string Summary => Profiles.Count(p => p.IsSelected) switch
    {
        0 => "Tick at least one profile.",
        1 => "1 profile selected.",
        var n => $"{n} profiles selected - copies all run; of the move profiles the last one that can place a book moves it.",
    };

    private bool CanRun() => Profiles.Any(p => p.IsSelected);

    /// <summary>The ticked profiles, in the order they are listed (which is the order they run in).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run() => _resolve(Profiles.Where(p => p.IsSelected).Select(p => p.Profile).ToList());

    /// <summary>Cancelling resolves with null - the caller aborts the run entirely rather than guessing a profile.</summary>
    [RelayCommand]
    private void Cancel() => _resolve(null);
}
