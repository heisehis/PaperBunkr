using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Organizing;

/// <summary>Picks the few comics the profile editor's live preview is drawn from: real comics from your library, from different series so
/// the templates are seen against varied data, without ever reading the whole library.</summary>
public static class OrganizerPreviewSample
{
    /// <param name="candidatePool">How many comics (lowest ids) to look through - bounded so a huge library never makes opening the editor slow.</param>
    public static List<Issue> Pick(PaperbunkrDbContext context, int count = 6, int candidatePool = 400) =>
        context.Issues.AsNoTracking()
            .Include(i => i.Series)
            .Where(i => i.FilePath != null && !i.IsPlaceholder)
            .OrderBy(i => i.Id)
            .Take(candidatePool)
            .AsEnumerable()
            .GroupBy(i => i.SeriesId)
            .Select(g => g.First())
            .Take(count)
            .ToList();
}
