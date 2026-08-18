using System.Linq;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One <see cref="RelationType"/> plus its human-readable label, for the relation-creation picker (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md).</summary>
public sealed record RelationTypeOption(RelationType Type, string Label)
{
    /// <summary>"SourceMaterial" -&gt; "Source Material" - a space before every interior capital, no per-value lookup table needed for ~27 values.</summary>
    public static string FormatLabel(RelationType type) => Regex.Replace(type.ToString(), "(?<!^)([A-Z])", " $1");

    public static readonly RelationTypeOption[] All = System.Enum.GetValues<RelationType>()
        .Select(t => new RelationTypeOption(t, FormatLabel(t)))
        .ToArray();
}
