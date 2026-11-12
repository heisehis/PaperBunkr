using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

public sealed record EventEvidence(EventMetadataField Field, string Value, string Provider, string SourceId,
    string Reason, string? SourceUrl = null, bool Automatic = true, int Quality = 1, bool IssueCover = false);
public sealed record EventSourceStatus(string Provider, string Status, string Detail);
public sealed record EventEnrichmentProposal(EventMetadataSnapshot Snapshot, IReadOnlyList<EventEvidence> Evidence,
    IReadOnlyList<EventSourceStatus> Sources)
{
    public IReadOnlyList<EventEvidence> Automatic => EventEnrichmentEvaluator.Select(this);
}
public sealed record EventApplyResult(Guid OperationId, int Changed, IReadOnlyList<string> Skipped, string? Identity = null);
