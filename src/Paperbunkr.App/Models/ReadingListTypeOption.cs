using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One <see cref="ReadingListType"/> plus its human-readable label, for the Reading List screen's type picker (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md). Same label-formatting approach as <see cref="RelationTypeOption"/>/<see cref="EventMembershipRoleOption"/>.</summary>
public sealed record ReadingListTypeOption(ReadingListType Type, string Label)
{
    public static string FormatLabel(ReadingListType type) => Regex.Replace(type.ToString(), "(?<!^)([A-Z])", " $1");

    public static readonly ReadingListTypeOption[] All = System.Enum.GetValues<ReadingListType>()
        .Select(t => new ReadingListTypeOption(t, FormatLabel(t)))
        .ToArray();
}
