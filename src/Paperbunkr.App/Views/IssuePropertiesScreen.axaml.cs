using Avalonia.Controls;
using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class IssuePropertiesScreen : UserControl
{
    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public IssuePropertiesScreen()
    {
        InitializeComponent();
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        _screenInput = ScreenInput.Attach(this, InputScope.Editor, new Dictionary<string, Func<bool>>
        {
            // Ctrl+S saves even from inside a text field; the editors close themselves, so the rebuild runs after the key press has finished routing.
            [InputActionIds.Save] = () => DataContext is IssuePropertiesScreenViewModel vm && vm.SaveCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.SaveCommand.Execute(null)),
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
        });
    }
}
