using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.SeriesConnector;

namespace Paperbunkr.App.Services;

/// <summary>
/// The small line under a Related card (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §6.1): why the card is there.
/// Your own relation shows its type; an automatic one shows its reason ("Later volume", "Annual", "Team-up title", "Metron", "GCD").
/// A stored reason describes the relation's source, so when the card is the target the wording flips ("Earlier volume", "Main series").
/// </summary>
public static class RelatedRelationLabels
{
    /// <param name="displayType">The type already resolved for the card's side (<c>MediaRelationEndpoint.DisplayType</c>).</param>
    /// <param name="cardIsSource">Whether the card's series is the relation's stored source.</param>
    public static string For(RelationType displayType, IReadOnlyCollection<RelationEvidence> evidence, bool cardIsSource)
    {
        bool fromGcd = evidence.Any(e => e.Provider == RelationEvidenceProvider.Gcd);
        string typeLabel = RelationTypeOption.FormatLabel(displayType);
        if (evidence.Count == 0 || evidence.Any(e => e.Provider == RelationEvidenceProvider.User))
        {
            return typeLabel + (fromGcd ? " · GCD" : string.Empty);
        }

        if (evidence.FirstOrDefault(e => e.Provider == RelationEvidenceProvider.Inferred && !string.IsNullOrWhiteSpace(e.ProviderRelationType)) is { } inferred)
        {
            string reason = cardIsSource ? inferred.ProviderRelationType! : TargetWording(inferred.ProviderRelationType!, displayType);
            return reason + (fromGcd ? " · GCD" : string.Empty);
        }

        if (evidence.Any(e => SeriesConnectorSweep.IsOwned(e) && e.Provider == RelationEvidenceProvider.Metron))
        {
            return "Metron";
        }

        return fromGcd ? typeLabel + " · GCD" : typeLabel;
    }

    /// <summary>The same reason, seen from the other end: the card is the earlier volume, or the series an annual sits beside.</summary>
    private static string TargetWording(string reason, RelationType displayType) => reason switch
    {
        VolumeChainInference.Reason => "Earlier volume",
        _ when displayType == RelationType.SideStory => "Main series",
        _ => reason,
    };
}
