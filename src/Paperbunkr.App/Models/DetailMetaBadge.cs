using System.Collections.Generic;
using FluentIcons.Common;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Models;

/// <summary>
/// One chip in the detail hero's metadata badge row (docs/superpowers/specs/2026-09-04-detail-
/// screen-icons-and-glyphs-design.md Part 4). Either a FluentIcons glyph + text
/// (<see cref="Icon"/>) or a resolved brand/metadata <see cref="BrandMark"/> (<see cref="Mark"/>).
/// </summary>
public sealed record DetailMetaBadge(string Text, Symbol? Icon = null, MarkFamily? Mark = null, string? MarkValue = null)
{
    public bool IsMark => Mark is not null;

    /// <summary>Non-null projections for the compiled bindings (which can't take a nullable enum);
    /// only the relevant one is actually rendered per <see cref="IsMark"/>.</summary>
    public Symbol IconGlyph => Icon ?? Symbol.Circle;
    public MarkFamily MarkOrDefault => Mark ?? MarkFamily.Publisher;

    /// <summary>
    /// The ordered badge set for a series/issue: publisher mark, publication-status glyph, issue/
    /// chapter count, unread count, year glyph, format mark, age-rating mark, language flag - each
    /// dropped when its source is blank. <paramref name="issueCountLabel"/> / <paramref name="unreadLabel"/>
    /// carry over the counts the original plain-text <c>MetaLine</c> used to show (e.g. "42 issues",
    /// "12 unread") - trailing optional params so existing callers built before they existed still
    /// compile.
    /// </summary>
    public static IReadOnlyList<DetailMetaBadge> Build(
        string? publisher, string? statusLabel, bool isComplete,
        string? year, string? format, string? ageRating, string? languageIso,
        string? issueCountLabel = null, string? unreadLabel = null)
    {
        var list = new List<DetailMetaBadge>();

        if (!string.IsNullOrWhiteSpace(publisher))
        {
            list.Add(new DetailMetaBadge(string.Empty, Mark: MarkFamily.Publisher, MarkValue: publisher));
        }

        if (!string.IsNullOrWhiteSpace(statusLabel))
        {
            list.Add(new DetailMetaBadge(statusLabel!, Icon: isComplete ? Symbol.CheckmarkCircle : Symbol.Circle));
        }

        if (!string.IsNullOrWhiteSpace(issueCountLabel))
        {
            list.Add(new DetailMetaBadge(issueCountLabel!, Icon: Symbol.TextBulletList));
        }

        if (!string.IsNullOrWhiteSpace(unreadLabel))
        {
            list.Add(new DetailMetaBadge(unreadLabel!, Icon: Symbol.CircleHalfFill));
        }

        if (!string.IsNullOrWhiteSpace(year))
        {
            list.Add(new DetailMetaBadge(year!, Icon: Symbol.Calendar));
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            list.Add(new DetailMetaBadge(string.Empty, Mark: MarkFamily.Format, MarkValue: format));
        }

        if (!string.IsNullOrWhiteSpace(ageRating))
        {
            list.Add(new DetailMetaBadge(string.Empty, Mark: MarkFamily.AgeRating, MarkValue: ageRating));
        }

        if (!string.IsNullOrWhiteSpace(languageIso))
        {
            list.Add(new DetailMetaBadge(string.Empty, Mark: MarkFamily.Language, MarkValue: languageIso));
        }

        return list;
    }
}
