using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maps each provider's own relation-type vocabulary onto this codebase's <see cref="RelationType"/>
/// enum (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §5) - a
/// best-effort lookup, not a claimed 1:1 semantic match; anything with no clean equivalent falls
/// back to <see cref="RelationType.Other"/> rather than guessing at a closer-sounding value.
/// </summary>
internal static class ProviderRelationTypeMapper
{
    private static readonly Dictionary<string, RelationType> AniList = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["ADAPTATION"] = RelationType.Adaptation,
        ["PREQUEL"] = RelationType.Prequel,
        ["SEQUEL"] = RelationType.Sequel,
        ["SIDE_STORY"] = RelationType.SideStory,
        ["SPIN_OFF"] = RelationType.SpinOff,
        ["ALTERNATIVE"] = RelationType.AlternateStory,
        ["SOURCE"] = RelationType.SourceMaterial,
        ["COMPILATION"] = RelationType.Compilation,
        ["CONTAINS"] = RelationType.Contains,
        ["PARENT"] = RelationType.Related,
        ["CHARACTER"] = RelationType.Other,
        ["SUMMARY"] = RelationType.Other,
        ["OTHER"] = RelationType.Other,
    };

    private static readonly Dictionary<string, RelationType> MangaBaka = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["adaptation"] = RelationType.Adaptation,
        ["alternative"] = RelationType.AlternateStory,
        ["cameo"] = RelationType.Other,
        ["character_focus"] = RelationType.Other,
        ["compilation"] = RelationType.Compilation,
        ["contains"] = RelationType.Contains,
        ["crossover"] = RelationType.Crossover,
        ["expansion"] = RelationType.Other,
        ["main"] = RelationType.Related,
        ["other"] = RelationType.Other,
        ["parent"] = RelationType.Related,
        ["parody"] = RelationType.Other,
        ["prequel"] = RelationType.Prequel,
        ["reboot"] = RelationType.Reboot,
        ["remake"] = RelationType.Remake,
        ["sequel"] = RelationType.Sequel,
        ["series"] = RelationType.Related,
        ["side_story"] = RelationType.SideStory,
        ["source"] = RelationType.SourceMaterial,
        ["spin_off"] = RelationType.SpinOff,
        ["summary"] = RelationType.Other,
        ["uncollected"] = RelationType.Other,
    };

    public static RelationType MapAniList(string? raw) =>
        raw is not null && AniList.TryGetValue(raw, out var type) ? type : RelationType.Other;

    public static RelationType MapMangaBaka(string? raw) =>
        raw is not null && MangaBaka.TryGetValue(raw, out var type) ? type : RelationType.Other;
}
