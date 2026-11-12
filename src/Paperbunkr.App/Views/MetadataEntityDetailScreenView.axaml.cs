using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>The entity page (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md). The tab buttons carry the <c>tab</c> class, so
/// <c>TabStrip.Step</c> drives them from the bumpers and Ctrl+PageUp/PageDown once the screen claims TabNext/TabPrevious.</summary>
public partial class MetadataEntityDetailScreenView : UserControl
{
    private readonly AttachedInputRegistration _input;
    public IInputService InputService { get => _input.Service; set => _input.Service = value; }

    public MetadataEntityDetailScreenView()
    {
        InitializeComponent();
        _input = ScreenInput.Attach(this, InputScope.Detail, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.Refresh] = () => DataContext is MetadataEntityDetailScreenViewModel vm && !vm.IsLoading && ScreenInput.Deferred(vm.Refresh),
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
        });
    }

    /// <summary>Opens the image picker in its own flyout on the Manage button, one tick later so the menu that raised the click has closed
    /// (CLAUDE.md: don't tear down a popup from inside an event it is still routing).</summary>
    private void OnChangeImage(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(ManageButton));
}
