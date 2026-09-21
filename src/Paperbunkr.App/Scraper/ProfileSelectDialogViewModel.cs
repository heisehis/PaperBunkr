using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Organizing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>One selectable profile row (design doc §7's "selectable per organize run, matching CE's
/// own profile-switch UX").</summary>
public sealed partial class ProfileSelectRowViewModel : ObservableObject
{
    private readonly Action<OrganizerProfile> _choose;

    public OrganizerProfile Profile { get; }
    public string Name => Profile.Name;

    public ProfileSelectRowViewModel(OrganizerProfile profile, Action<OrganizerProfile> choose)
    {
        Profile = profile;
        _choose = choose;
    }

    [RelayCommand]
    private void Choose() => _choose(Profile);
}

/// <summary>
/// Backs <see cref="ProfileSelectDialogView"/> - shown before a manual "Organize Library" run when
/// more than one <c>OrganizerProfile</c> exists (design doc §7: multiple named profiles are a
/// headline feature, but nothing previously let a user pick which one a given run used - it always
/// silently ran whichever profile happened to load first). Same modal-per-run mechanism as
/// <see cref="FileConflictDialogViewModel"/>/<see cref="ComicVineMatchReviewDialogViewModel"/>.
/// </summary>
public sealed partial class ProfileSelectDialogViewModel : ObservableObject
{
    private readonly Action<OrganizerProfile?> _resolve;

    public IReadOnlyList<ProfileSelectRowViewModel> Profiles { get; }

    public ProfileSelectDialogViewModel(IReadOnlyList<OrganizerProfile> profiles, Action<OrganizerProfile?> resolve)
    {
        _resolve = resolve;
        Profiles = profiles.Select(p => new ProfileSelectRowViewModel(p, Choose)).ToList();
    }

    private void Choose(OrganizerProfile profile) => _resolve(profile);

    /// <summary>Cancelling resolves with null - the caller aborts the organize run entirely rather
    /// than falling back to a guessed profile.</summary>
    [RelayCommand]
    private void Cancel() => _resolve(null);
}
