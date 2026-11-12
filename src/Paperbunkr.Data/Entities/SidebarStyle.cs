namespace Paperbunkr.Data.Entities;

/// <summary>How a selected sidebar row is drawn (docs/superpowers/specs/2026-11-06-personalization-design.md). Stored as an int - append only.</summary>
public enum SidebarStyle
{
    /// <summary>A 3 px accent bar plus a soft tint (today's look).</summary>
    Standard = 0,

    /// <summary>A rounded fill, no bar.</summary>
    Pill = 1,

    /// <summary>A tint only, no bar.</summary>
    Tint = 2,
}
