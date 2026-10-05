using System.Collections.Generic;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The serialisable keymap (docs/superpowers/specs/2026-10-03-input-service-design.md §5.3), stored as <c>keymap.json</c>. It holds only what the user changed:
/// the defaults live in <see cref="InputActions"/> in code, so a new default shipped in a later release still reaches every action the user did not customise.
/// </summary>
public sealed class KeymapConfig
{
    /// <summary>The schema this build writes and understands. Bump it together with an <see cref="IKeymapMigration"/> that upgrades the previous shape.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Action id to its bindings in text form. An entry <em>replaces</em> that action's whole default list; an empty list means "deliberately unbound". Entries for
    /// actions this build does not know (an uninstalled plugin, a removed action) are kept and written back untouched.
    /// </summary>
    public Dictionary<string, List<string>> Overrides { get; set; } = [];

    /// <summary>Thresholds and timings the input service uses; the defaults reproduce the values that were hardcoded before the service existed.</summary>
    public InputTuning Tuning { get; set; } = new();

    /// <summary>Set once the old <c>KeyBinding</c> database rows have been imported, so the import never runs twice.</summary>
    public bool LegacyBindingsImported { get; set; }
}

/// <summary>Numbers the service needs that used to be constants scattered through the window and the gamepad mapper.</summary>
public sealed class InputTuning
{
    /// <summary>How far a horizontal wheel/touchpad delta must reach before it counts as a left/right swipe binding (one large delta, not accumulated scrolling).</summary>
    public double SwipeThreshold { get; set; } = 1.5;

    /// <summary>Fraction of a stick's range ignored around centre (radial dead zone).</summary>
    public double StickDeadZone { get; set; } = 0.25;

    /// <summary>How far the left stick must be pushed to count as a D-pad press.</summary>
    public double StickDigitalThreshold { get; set; } = 0.5;

    /// <summary>Dead zone of the analogue triggers, as a fraction of their range.</summary>
    public double TriggerDeadZone { get; set; } = 0.12;

    /// <summary>A held pad button repeats after this long...</summary>
    public double RepeatInitialMs { get; set; } = 400;

    /// <summary>...and then every this often.</summary>
    public double RepeatIntervalMs { get; set; } = 90;
}
