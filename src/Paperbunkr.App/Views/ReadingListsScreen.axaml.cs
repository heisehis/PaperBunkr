using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class ReadingListsScreen : UserControl
{
    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public ReadingListsScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.ReadingLists, new Dictionary<string, Func<bool>>
        {
            // The same command the gallery's "New reading list" button runs; it lives on the shell's view model, which owns the dialog.
            [InputActionIds.NewItem] = () => TopLevel.GetTopLevel(this) is Window { DataContext: MainViewModel main }
                                             && main.OpenNewReadingListDialogCommand.CanExecute(null)
                                             && ScreenInput.Deferred(() => main.OpenNewReadingListDialogCommand.Execute(null)),
            [InputActionIds.Refresh] = () => DataContext is ReadingListsScreenViewModel vm && ScreenInput.Deferred(vm.RefreshSidebar),
        });
    }
}
