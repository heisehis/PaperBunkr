using System.Linq;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Upserts <see cref="IssueVariantCover"/> rows for one issue from Metron's <c>variants</c> field
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md). Idempotent.
///
/// <b>Scope note:</b> this stores the real variant data (name/image) fetched during a normal scrape -
/// it's wired into the scrape pipeline the same as the other resolvers, so the rows are genuinely
/// populated, not just theoretical. What isn't built in this pass is the cover-picker UI actually
/// letting a user pick one of these as the issue's displayed cover - that's a separate UI feature
/// using this data, not attempted here.
/// </summary>
public static class IssueVariantCoverSync
{
    public static void SyncFromIssueDetails(PaperbunkrDbContext context, int issueId, IReadOnlyList<ComicVineVariantCover> variants)
    {
        var existing = context.IssueVariantCovers.Where(v => v.IssueId == issueId).ToList();
        var wantedUrls = variants.Select(v => v.ImageUrl).ToHashSet();

        foreach (var stale in existing.Where(v => !wantedUrls.Contains(v.ImageUrl)))
        {
            context.IssueVariantCovers.Remove(stale);
        }

        foreach (var variant in variants)
        {
            var row = existing.FirstOrDefault(v => v.ImageUrl == variant.ImageUrl);
            if (row is null)
            {
                context.IssueVariantCovers.Add(new IssueVariantCover { IssueId = issueId, Name = variant.Name, ImageUrl = variant.ImageUrl });
            }
            else if (row.Name != variant.Name)
            {
                row.Name = variant.Name;
            }
        }

        context.SaveChanges();
    }
}
