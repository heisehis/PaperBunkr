using Avalonia.Controls;
using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class DetailScreen : UserControl
{
    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public DetailScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.Detail, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
            [InputActionIds.DetailBack] = () => DataContext is DetailScreenViewModel vm && vm.GoBackCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.GoBackCommand.Execute(null)),
            [InputActionIds.DetailContinue] = () => DataContext is DetailScreenViewModel vm && vm.ContinueCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.ContinueCommand.Execute(null)),
            [InputActionIds.DetailEdit] = () => DataContext is DetailScreenViewModel vm && vm.EditCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.EditCommand.Execute(null)),
        });
        DetailCosmetics.Attach(this);
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
    }
}
