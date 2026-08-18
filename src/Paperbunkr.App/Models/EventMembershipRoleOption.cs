using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One <see cref="EventMembershipRole"/> plus its human-readable label, for the Events screen's role picker (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-design.md). Same label-formatting approach as <see cref="RelationTypeOption"/>.</summary>
public sealed record EventMembershipRoleOption(EventMembershipRole Role, string Label)
{
    public static string FormatLabel(EventMembershipRole role) => Regex.Replace(role.ToString(), "(?<!^)([A-Z])", " $1");

    public static readonly EventMembershipRoleOption[] All = System.Enum.GetValues<EventMembershipRole>()
        .Select(r => new EventMembershipRoleOption(r, FormatLabel(r)))
        .ToArray();
}
