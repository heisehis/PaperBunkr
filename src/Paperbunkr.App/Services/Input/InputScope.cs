namespace Paperbunkr.App.Services.Input;

/// <summary>
/// A named layer of input handling that is active while something is registered against it (docs/superpowers/specs/2026-10-03-input-service-design.md §5.2).
/// There is no scope stack: a scope is active exactly while it has at least one live registration, so registrations can be disposed in any order. Active
/// scopes are consulted highest <see cref="Priority"/> first, <see cref="Global"/> last. When an active scope <see cref="IsModal"/>, only the highest modal
/// scope and <see cref="Global"/> are consulted, so an open overlay cannot be driven by the screen behind it while a Global action such as
/// <see cref="InputActionIds.CloseCurrentView"/> still works.
/// </summary>
/// <remarks>
/// An open set: anything may declare its own scope. Use the statics below rather than <c>new</c>, since the record's equality covers all three fields,
/// so a scope built with a different priority than the standard one is a different scope.
/// </remarks>
/// <param name="Name">Identifies the scope, e.g. "Reader".</param>
/// <param name="Priority">Higher is consulted first.</param>
/// <param name="IsModal">True when lower, non-global scopes must not see input while this one is active.</param>
public readonly record struct InputScope(string Name, int Priority, bool IsModal = false)
{
    /// <summary>Priority of <see cref="Global"/>: always active, consulted last.</summary>
    public const int GlobalPriority = 0;

    /// <summary>Priority of the per-screen scopes.</summary>
    public const int ScreenPriority = 100;

    /// <summary>Priority of <see cref="Overlay"/>: above any screen.</summary>
    public const int OverlayPriority = 200;

    /// <summary>Active for the whole life of the app, whether or not anything is registered against it.</summary>
    public static readonly InputScope Global = new("Global", GlobalPriority);

    public static readonly InputScope Library = new("Library", ScreenPriority);
    public static readonly InputScope Books = new("Books", ScreenPriority);
    public static readonly InputScope ReadingLists = new("ReadingLists", ScreenPriority);

    /// <summary>The comic page reader.</summary>
    public static readonly InputScope Reader = new("Reader", ScreenPriority);

    /// <summary>The EPUB and text book reader.</summary>
    public static readonly InputScope BookReader = new("BookReader", ScreenPriority);

    public static readonly InputScope PdfReader = new("PdfReader", ScreenPriority);

    /// <summary>The side-by-side issue comparison.</summary>
    public static readonly InputScope Compare = new("Compare", ScreenPriority);

    /// <summary>A dialog or overlay panel; modal, so the screen underneath stops receiving input while it is open.</summary>
    public static readonly InputScope Overlay = new("Overlay", OverlayPriority, IsModal: true);

    /// <summary>True for <see cref="Global"/>, which is never deactivated and never suppressed by a modal scope.</summary>
    public bool IsGlobal => Priority == GlobalPriority && Name == "Global";

    public override string ToString() => Name;
}
