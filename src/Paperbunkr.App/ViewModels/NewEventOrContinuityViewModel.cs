using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The create/edit dialog for the Events &amp; Continuity screen
/// (docs/superpowers/specs/2026-08-28-events-continuity-screen-redesign-design.md and
/// docs/superpowers/specs/2026-08-28-continuity-editing-design.md). Handles both "New event" /
/// "New continuity" and, via <see cref="LoadForEdit"/>, "Edit details" for an existing event or
/// continuity - name, description, and (continuity only) publisher. Class name is historical; it
/// is no longer new-only.
/// </summary>
public partial class NewEventOrContinuityViewModel : ViewModelBase
{
    public enum Kind { Event, Continuity }

    private readonly Action<Kind, int> _onSaved;
    private readonly Action _onCancel;

    public NewEventOrContinuityViewModel(Action<Kind, int> onSaved, Action onCancel)
    {
        _onSaved = onSaved;
        _onCancel = onCancel;
    }

    /// <summary>Puts the dialog into create mode for a brand-new event or continuity.</summary>
    public void Reset(Kind kind)
    {
        IsEdit = false;
        _editId = 0;
        CurrentKind = kind;
        Name = kind == Kind.Event ? "New Story Event" : "New Continuity";
        Publisher = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Puts the dialog into edit mode, pre-filled from an existing entity.</summary>
    public void LoadForEdit(Kind kind, int id)
    {
        using var context = PaperbunkrDb.CreateContext();

        if (kind == Kind.Event)
        {
            var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == id);
            if (storyEvent is null)
            {
                return;
            }

            Name = storyEvent.Name;
            Description = storyEvent.Description ?? string.Empty;
            Publisher = string.Empty;
        }
        else
        {
            var continuity = context.Continuities.FirstOrDefault(c => c.Id == id);
            if (continuity is null)
            {
                return;
            }

            Name = continuity.Name;
            Description = continuity.Description ?? string.Empty;
            Publisher = continuity.Publisher ?? string.Empty;
        }

        CurrentKind = kind;
        _editId = id;
        IsEdit = true;
    }

    private int _editId;

    [ObservableProperty]
    private bool _isEdit;

    partial void OnIsEditChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SaveButtonLabel));
    }

    [ObservableProperty]
    private Kind _currentKind;

    partial void OnCurrentKindChanged(Kind value)
    {
        OnPropertyChanged(nameof(IsContinuity));
        OnPropertyChanged(nameof(Title));
    }

    public bool IsContinuity => CurrentKind == Kind.Continuity;

    public string Title => (IsEdit, CurrentKind) switch
    {
        (true, Kind.Event) => "EDIT EVENT",
        (true, Kind.Continuity) => "EDIT CONTINUITY",
        (false, Kind.Event) => "NEW EVENT",
        _ => "NEW CONTINUITY",
    };

    public string SaveButtonLabel => IsEdit ? "Save" : "Create";

    [ObservableProperty]
    private string _name = string.Empty;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public bool CanCreate => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void Create()
    {
        if (!CanCreate)
        {
            return;
        }

        string name = Name.Trim();
        string? description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        string? publisher = string.IsNullOrWhiteSpace(Publisher) ? null : Publisher.Trim();
        var now = DateTime.UtcNow;

        using var context = PaperbunkrDb.CreateContext();

        if (CurrentKind == Kind.Event)
        {
            if (IsEdit)
            {
                var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == _editId);
                if (storyEvent is null)
                {
                    return;
                }

                storyEvent.Name = name;
                storyEvent.Description = description;
                storyEvent.UpdatedAt = now;
                context.SaveChanges();
                _onSaved(Kind.Event, storyEvent.Id);
            }
            else
            {
                var storyEvent = new StoryEvent { Name = name, Description = description, CreatedAt = now, UpdatedAt = now };
                context.StoryEvents.Add(storyEvent);
                context.SaveChanges();
                _onSaved(Kind.Event, storyEvent.Id);
            }
        }
        else
        {
            if (IsEdit)
            {
                var continuity = context.Continuities.FirstOrDefault(c => c.Id == _editId);
                if (continuity is null)
                {
                    return;
                }

                continuity.Name = name;
                continuity.Description = description;
                continuity.Publisher = publisher;
                continuity.UpdatedAt = now;
                context.SaveChanges();
                _onSaved(Kind.Continuity, continuity.Id);
            }
            else
            {
                var continuity = new Continuity
                {
                    Name = name,
                    Publisher = publisher,
                    Description = description,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.Continuities.Add(continuity);
                context.SaveChanges();
                _onSaved(Kind.Continuity, continuity.Id);
            }
        }
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
