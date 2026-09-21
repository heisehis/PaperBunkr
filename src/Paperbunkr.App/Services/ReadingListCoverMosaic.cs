using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// Picks which member covers make up a reading list's header mosaic (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-design.md #7). Pure so the fill rule is unit-testable: one or two covers stay a single
/// cover (a half-empty 2x2 reads as broken), three repeat the first to fill the grid, four or more use the
/// first four. Keys are <see cref="CoverFingerprint"/> stems, the same identity the Library's covers use.
/// </summary>
public static class ReadingListCoverMosaic
{
    public const int GridSize = 4;

    public static IReadOnlyList<string> PickCoverKeys(IReadOnlyList<string> memberCoverKeys)
    {
        ArgumentNullException.ThrowIfNull(memberCoverKeys);

        return memberCoverKeys.Count switch
        {
            0 => Array.Empty<string>(),
            1 or 2 => new[] { memberCoverKeys[0] },
            3 => new[] { memberCoverKeys[0], memberCoverKeys[1], memberCoverKeys[2], memberCoverKeys[0] },
            _ => new[] { memberCoverKeys[0], memberCoverKeys[1], memberCoverKeys[2], memberCoverKeys[3] },
        };
    }
}
