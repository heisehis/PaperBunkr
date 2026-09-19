namespace Paperbunkr.Daemon.Indexers;

/// <summary>
/// Runs the cascading search for one wanted issue (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-
/// design.md §5): the strict pass first, and only when it yields <b>zero accepted</b> releases does it fall back
/// to the sanitized alias pass. The first pass that produces an accepted hit stops the search.
/// </summary>
public sealed class ReleaseSearcher(IIndexerClient client, ScoringOptions options)
{
    public async Task<IReadOnlyList<ScoredRelease>> SearchAsync(IndexerQuery query, CancellationToken cancellationToken)
    {
        foreach (var pass in QueryBuilder.Build(query))
        {
            var accepted = new Dictionary<string, ScoredRelease>(StringComparer.Ordinal);

            foreach (var text in pass.Queries)
            {
                var raw = await client.SearchAsync(text, cancellationToken).ConfigureAwait(false);

                foreach (var release in raw)
                {
                    var scored = ReleaseEvaluator.Evaluate(release, query, options);
                    if (scored is null)
                    {
                        continue;
                    }

                    var key = release.Guid ?? release.DownloadUrl;
                    if (!accepted.TryGetValue(key, out var existing) || scored.Score > existing.Score)
                    {
                        accepted[key] = scored;
                    }
                }

                if (accepted.Count > 0)
                {
                    break; // an accepted hit stops the number-variant search
                }
            }

            if (accepted.Count > 0)
            {
                return accepted.Values.OrderByDescending(r => r.Score).ToList();
            }
        }

        return Array.Empty<ScoredRelease>();
    }
}
