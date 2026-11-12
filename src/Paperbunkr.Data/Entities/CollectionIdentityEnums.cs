namespace Paperbunkr.Data.Entities;

/// <summary>How a collection's cover is drawn on Home cards and the Edit Collection preview (docs/superpowers/specs/2026-11-06-collection-identity-design.md).
/// A manual cover file always overrides it. Stored as an int - append only.</summary>
public enum CollectionCoverStyle
{
    /// <summary>Today's rule: a 2x2 mosaic from three or more distinct covers, else the single cover.</summary>
    Auto = 0,

    Single = 1,

    /// <summary>2x2 mosaic; falls back to Single below three distinct covers.</summary>
    Mosaic = 2,

    /// <summary>Up to three overlapping covers, slightly rotated; falls back to Single below two distinct covers.</summary>
    Fan = 3,
}

/// <summary>Row height of the sidebar's Collections section. Stored as an int - append only.</summary>
public enum CollectionRowDensity
{
    Comfortable = 0,
    Compact = 1,
}

/// <summary>Shape of a collection's sidebar marker when it has no glyph. Never an oval pill. Stored as an int - append only.</summary>
public enum CollectionDotShape
{
    Circle = 0,
    Squircle = 1,
}
