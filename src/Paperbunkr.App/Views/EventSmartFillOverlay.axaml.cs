using Avalonia.Controls;
using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class EventSmartFillOverlay : UserControl
{
    private readonly AttachedInputRegistration _input;
    public IInputService InputService { get => _input.Service; set => _input.Service = value; }
    public EventSmartFillOverlay()
    {
        InitializeComponent();
        var shell = this.FindControl<OverlayShell>("FillShell")!;
        _input = ScreenInput.Attach(shell, InputScope.Overlay, new Dictionary<string, Func<bool>>());
        shell.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !shell.IsVisible && DataContext is EventSmartFillViewModel vm)
                vm.ReleaseClosedPreviews();
        };
    }
}
