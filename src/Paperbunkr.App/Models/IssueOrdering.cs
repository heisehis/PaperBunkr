using System.Collections.Generic;
using System.Linq;
using cYo.Common.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// Orders issues by their actual issue <see cref="Issue.Number"/> rather than database insertion
/// order (<c>Id</c>). <c>Number</c> is a nullable, not-strictly-numeric string (e.g. "1", "10",
/// "0.5", "Annual 1"), so this reuses <see cref="TextNumberFloat"/> - the same numeric-aware
/// comparison already ported into Paperbunkr.Common/used by ComicBookNumberComparer in
/// Paperbunkr.Engine - rather than a naive numeric or ordinal string sort.
/// </summary>
public static class IssueOrdering
{
    public static IOrderedEnumerable<Issue> OrderByNumber(this IEnumerable<Issue> issues) =>
        issues.OrderBy(i => new TextNumberFloat(i.EffectiveNumber() ?? string.Empty));
}
