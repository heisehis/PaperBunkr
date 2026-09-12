using System.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Wraps a real <see cref="IActivityJobHandle"/> as the dependency-clean
/// <see cref="IPluginActivityHandle"/> a native plugin actually receives (docs/superpowers/specs/
/// 2026-09-11-plugin-api-v4-native-tier-design.md §3, implementation plan Phase 1 Step 1.5) - the
/// real interface exposes <c>ActivityJob</c> (an <c>ObservableObject</c> in
/// <c>Paperbunkr.App.Models</c>), which <c>Paperbunkr.Plugins.Abstractions</c> can never reference.
/// </summary>
internal sealed class PluginActivityHandleAdapter : IPluginActivityHandle
{
    private readonly IActivityJobHandle _inner;

    public PluginActivityHandleAdapter(IActivityJobHandle inner)
    {
        _inner = inner;
    }

    public CancellationToken CancellationToken => _inner.CancellationToken;

    public void Report(int done, int total, string? detail = null) => _inner.Report(done, total, detail);

    public void Succeed(string summary) => _inner.Succeed(summary);

    public void Fail(string summary) => _inner.Fail(summary);

    public void Dispose() => _inner.Dispose();
}
