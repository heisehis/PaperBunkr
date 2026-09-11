using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Abstractions.Native;

/// <summary>
/// Full-trust environment a native plugin receives (docs/superpowers/specs/2026-09-11-plugin-api-v4-
/// native-tier-design.md §2/§3) — a strictly wider surface than the base <see cref="IPluginEnvironment"/>
/// scripts get, never the other way around; nothing about this tier loosens the existing `.csx` sandbox.
/// </summary>
public interface INativePluginEnvironment : IPluginEnvironment
{
    /// <summary>Opens a new, real <see cref="PaperbunkrDbContext"/> for this plugin's own use — full
    /// read/write access, no <c>IMetadataWriter</c> audit gate (v4 §2's deliberate full-trust call).
    /// A factory, not a shared instance, matching the app's own <c>PaperbunkrDb.CreateContext</c>
    /// idiom rather than handing out one long-lived context.</summary>
    Func<PaperbunkrDbContext> CreateDbContext { get; }

    /// <summary>Starts a real, host-tracked Activity Center job (kind, title, cancellable) and returns
    /// a dependency-clean progress handle — see <see cref="IPluginActivityHandle"/> for why this isn't
    /// the real <c>IActivityJobHandle</c> itself.</summary>
    Func<ActivityJobKind, string, bool, IPluginActivityHandle> StartActivityJob { get; }
}

/// <summary>
/// Dependency-clean progress-reporting surface for native plugins. Deliberately NOT the real
/// <c>IActivityJobHandle</c> (<c>Paperbunkr.App.Services</c>), which exposes an <c>ActivityJob</c> —
/// an <c>ObservableObject</c> living in <c>Paperbunkr.App.Models</c>. <c>Paperbunkr.Plugins.Abstractions</c>
/// must never reference <c>Paperbunkr.App</c> (that project references this one) — a first attempt to
/// expose the real interface directly here didn't compile for exactly that reason. The host's real
/// <see cref="INativePluginEnvironment"/> implementation wraps a real <c>IActivityJobHandle</c> in a
/// small adapter implementing this interface instead.
/// </summary>
public interface IPluginActivityHandle : IDisposable
{
    CancellationToken CancellationToken { get; }

    void Report(int done, int total, string? detail = null);

    void Succeed(string summary);

    void Fail(string summary);
}
