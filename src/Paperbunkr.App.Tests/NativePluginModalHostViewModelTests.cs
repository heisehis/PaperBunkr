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
