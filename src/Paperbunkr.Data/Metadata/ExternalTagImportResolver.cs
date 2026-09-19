using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Bulk per-series tag import from provider metadata into the existing weighted/categorized
/// <see cref="IssueTag"/> model (docs/superpowers/specs/2026-09-18-external-metadata-full-
/// extraction-design.md §4) - provider tag data is Series-scoped, <see cref="IssueTag"/> is
/// Issue-scoped, so this writes the same tag set to every current Issue in the series (explicit
/// user instruction: bulk, overwriting where the provider's set differs, not a non-destructive
/// merge). Reuses <see cref="IssueTagExtensions.MergeFromCategorized"/>'s diff-not-replace
/// mechanics per issue, so a hand-set Weight on a value that survives the diff is never touched.
/// </summary>
public static class ExternalTagImportResolver
{
    /// <summary>Caller is responsible for <c>SaveChanges()</c> - matches
    /// <see cref="MetadataLinkResolver.LinkAsync"/>'s own single-save-per-link-call convention.</summary>
    public static void ApplyToSeries(PaperbunkrDbContext context, Series series, ExternalMediaMetadata metadata)
    {
        bool hasGenreTags = metadata.GenreTags is { Count: > 0 };
        bool hasOtherTags = metadata.OtherTags is { Count: > 0 };
        if (!hasGenreTags && !hasOtherTags)
        {
            return;
        }

        var issues = context.Issues
            .Where(i => i.SeriesId == series.Id)
            .Include(i => i.Tags)
            .ToList();

        foreach (var issue in issues)
        {
            if (hasGenreTags)
            {
                issue.MergeFromCategorized(IssueTagField.Genre, metadata.GenreTags!.Select(v => (v, "Genre")));
            }

            if (hasOtherTags)
            {
                issue.MergeFromCategorized(IssueTagField.Tags, metadata.OtherTags!);
            }
        }
    }
}
