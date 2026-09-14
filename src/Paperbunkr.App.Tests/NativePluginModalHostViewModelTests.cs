using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Implementation plan (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-plan.md)
/// Step 4 verification: <see cref="NativePluginModalHostViewModel.Advance"/> opportunistically
/// disposes an outgoing hosted control's <c>DataContext</c>/content when either implements
/// <see cref="IDisposable"/>, and does nothing (no exception) when neither does.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class NativePluginModalHostViewModelTests
{
    [Fact]
    public async Task Dismissing_disposes_a_disposable_DataContext()
    {
        var vm = new NativePluginModalHostViewModel();
        var fake = new FakeDisposable();
        var content = new Border { DataContext = fake };

        Task task = vm.ShowAsync<object?>(_ => content);
        vm.DismissCommand.Execute(null);

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task Dismissing_content_that_implements_IDisposable_directly_also_disposes_it()
    {
        var vm = new NativePluginModalHostViewModel();
        var content = new FakeDisposableControl();

        Task task = vm.ShowAsync<object?>(_ => content);
        vm.DismissCommand.Execute(null);

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.True(content.Disposed);
    }

    [Fact]
    public void Dismissing_content_with_neither_disposable_does_not_throw()
    {
        var vm = new NativePluginModalHostViewModel();
        var content = new Border();

        _ = vm.ShowAsync<object?>(_ => content);
        vm.DismissCommand.Execute(null); // must not throw
    }

    /// <summary>
    /// docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-plan.md Step 1 verification:
    /// <see cref="NativePluginModalHostViewModel.BeginBatch"/> keeps the shell open and the header
    /// mounted across a sequence of otherwise-independent <see cref="NativePluginModalHostViewModel.ShowAsync{TResult}"/>
    /// calls, instead of each one tearing the shell down between items.
    /// </summary>
    [Fact]
    public void IsOpen_and_HeaderContent_persist_across_two_sequential_calls_inside_a_batch()
    {
        var vm = new NativePluginModalHostViewModel();
        var header = new Border();
        using IDisposable batch = vm.BeginBatch(header);

        Task first = vm.ShowAsync<object?>(resolve => new Border());
        Assert.True(vm.IsOpen);
        Assert.Same(header, vm.HeaderContent);

        vm.DismissCommand.Execute(null); // resolves/cancels the first item, same as a real per-book dialog closing

        // No second item queued yet - a plugin's per-item loop awaits one result before starting the
        // next ShowAsync call, so there's a real gap here. The shell must stay open regardless.
        Assert.True(vm.IsOpen);
        Assert.Same(header, vm.HeaderContent);

        Task second = vm.ShowAsync<object?>(resolve => new Border());
        Assert.True(vm.IsOpen);
        Assert.Same(header, vm.HeaderContent);

        vm.DismissCommand.Execute(null);
    }

    [Fact]
    public void Disposing_an_idle_batch_closes_the_shell_and_clears_the_header()
    {
        var vm = new NativePluginModalHostViewModel();
        var header = new Border();
        IDisposable batch = vm.BeginBatch(header);

        Task shown = vm.ShowAsync<object?>(resolve => new Border());
        vm.DismissCommand.Execute(null); // no next item coming - batch is now idle

        batch.Dispose();

        Assert.False(vm.IsOpen);
        Assert.Null(vm.HeaderContent);
    }

    [Fact]
    public void Disposing_a_batch_with_a_modal_still_pending_does_not_close_the_shell()
    {
        var vm = new NativePluginModalHostViewModel();
        var header = new Border();
        IDisposable batch = vm.BeginBatch(header);

        Task pending = vm.ShowAsync<object?>(resolve => new Border());
        batch.Dispose();

        Assert.True(vm.IsOpen);
        Assert.Null(vm.HeaderContent);

        vm.DismissCommand.Execute(null);
    }

    private sealed class FakeDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeDisposableControl : Border, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
