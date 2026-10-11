using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The one place that decides whether a newly created <see cref="MetadataProposal"/> applies itself or waits for review
/// (docs/superpowers/specs/2026-10-06-smart-features-design.md §6.1): the Automatic policy, gated by
/// <see cref="AppSettings.AutoApplyMinConfidence"/>. At the default threshold of 0 this is the policy alone, as before.
/// </summary>
public static class MetadataAutoApply
{
    public static bool ShouldApply(AppSettings settings, decimal confidence) =>
        settings.MetadataResolutionPolicy == MetadataResolutionPolicy.Automatic
        && confidence >= ClampThreshold(settings.AutoApplyMinConfidence);

    /// <summary>The threshold as a 0-1 value whatever was stored.</summary>
    public static decimal ClampThreshold(decimal value) => Math.Clamp(value, 0m, 1m);
}
