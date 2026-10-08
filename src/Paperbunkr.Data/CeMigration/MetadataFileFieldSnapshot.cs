using System;
using System.Text;
using cYo.Projects.ComicRack.Engine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// A value snapshot of everything the file metadata write-back would put on disk for an
/// <see cref="Issue"/> - the mapped <c>ComicInfo.xml</c> content plus the
/// <see cref="PaperbunkrSidecar"/> content. Trigger sites capture one before an edit and one after,
/// and only enqueue a write when <see cref="Differ"/> is true (docs/superpowers/specs/2026-09-03-
/// file-metadata-write-back-design.md) - generalizing the ad-hoc <c>genreBefore/genreAfter</c>
/// compare that <c>IssuePropertiesScreenViewModel.Save</c> used to do for Genre/Tags only.
///
/// Built by running the real <see cref="IssueToComicInfoMapper"/> / <see cref="PaperbunkrSidecar"/>
/// so the field list never drifts from what actually gets written: if the write is a no-op, so is
/// the snapshot compare.
/// </summary>
public sealed record MetadataFileFieldSnapshot(string ComicInfoContent, string SidecarContent, string MetronInfoContent)
{
    public static MetadataFileFieldSnapshot Capture(Issue issue)
    {
        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(issue, info);

        // MetronInfo.xml carries a few fields the other two files have no home for (UPC, ISBN). It is
        // captured whether or not the setting is on, since the callers have no settings in hand, and
        // without provider ids, which no metadata editor changes. Worst case with the setting off:
        // an edit to one of those fields queues a write that rewrites the same ComicInfo.xml.
        var metron = new MetronInfo();
        IssueToMetronInfoMapper.Apply(issue, metron, MetronIdContext.Empty);

        return new MetadataFileFieldSnapshot(
            ComicInfoContent: Encoding.UTF8.GetString(info.ToArray()),
            SidecarContent: PaperbunkrSidecar.FromIssue(issue).ToCanonicalString(),
            MetronInfoContent: Encoding.UTF8.GetString(metron.ToArray()));
    }

    /// <summary>True when a write would produce different file content than <paramref name="before"/> did.</summary>
    public static bool Differ(MetadataFileFieldSnapshot before, MetadataFileFieldSnapshot after) =>
        !string.Equals(before.ComicInfoContent, after.ComicInfoContent, StringComparison.Ordinal)
        || !string.Equals(before.SidecarContent, after.SidecarContent, StringComparison.Ordinal)
        || !string.Equals(before.MetronInfoContent, after.MetronInfoContent, StringComparison.Ordinal);
}
