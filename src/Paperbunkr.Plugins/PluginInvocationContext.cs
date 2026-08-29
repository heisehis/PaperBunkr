namespace Paperbunkr.Plugins;

/// <summary>
/// Ambient per-invocation state for the confirmation gate on bulk metadata writes
/// (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §5). Flows across the
/// script's <c>await</c>s via <see cref="AsyncLocal{T}"/>, set for the lifetime of one
/// <see cref="Command.InvokeAsync"/> call.
///
/// A command whose manifest declares <c>confirmWrites="true"</c> starts with
/// <see cref="RequiresWriteConfirmation"/> = true and <see cref="WritesConfirmed"/> = false; the
/// <c>IApplication.AskQuestion</c> adapter flips <see cref="WritesConfirmed"/> when the user answers
/// affirmatively, and the <c>IMetadataWriter</c> adapter refuses to write until it is set.
/// </summary>
public sealed class PluginInvocationContext
{
    private static readonly AsyncLocal<PluginInvocationContext?> CurrentContext = new();

    private PluginInvocationContext(string pluginKey, bool requiresWriteConfirmation)
    {
        PluginKey = pluginKey;
        RequiresWriteConfirmation = requiresWriteConfirmation;
    }

    /// <summary>The context for the currently-running plugin command invocation, or null outside one.</summary>
    public static PluginInvocationContext? Current => CurrentContext.Value;

    /// <summary>Key of the plugin whose command is running — for audit logging and per-plugin scoping.</summary>
    public string PluginKey { get; }

    public bool RequiresWriteConfirmation { get; }

    /// <summary>Set true once the command has obtained an affirmative <c>AskQuestion</c> answer this invocation.</summary>
    public bool WritesConfirmed { get; set; }

    /// <summary>
    /// True when an <c>IMetadataWriter</c> call is allowed right now: either the command didn't
    /// declare <c>confirmWrites</c>, or it did and the user has already confirmed this invocation.
    /// </summary>
    public bool WritesAllowed => !RequiresWriteConfirmation || WritesConfirmed;

    /// <summary>Enters a new invocation scope; dispose to restore the previous one.</summary>
    public static Scope Enter(string pluginKey, bool requiresWriteConfirmation)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = new PluginInvocationContext(pluginKey, requiresWriteConfirmation);
        return new Scope(previous);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly PluginInvocationContext? _previous;

        internal Scope(PluginInvocationContext? previous) => _previous = previous;

        public void Dispose() => CurrentContext.Value = _previous;
    }
}
