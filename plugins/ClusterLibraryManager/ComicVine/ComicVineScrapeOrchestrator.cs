using ClusterLibraryManager.Settings;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace ClusterLibraryManager.ComicVine;

/// <summary>
/// Ties <see cref="ComicVineService"/>, <see cref="MatchScoreCalculator"/>, and
/// <see cref="ComicVineMatchMemory"/> together into the actual "scrape this book" flow (design doc
/// §4) - search by series name, rank candidates, resolve via the review dialog (interactive) or
/// auto-choose/skip-and-log (design §4/§9's headless-automation gate), then apply.
///
/// <b>Scoped-down first pass, not the full CE-parity apply pipeline</b> - flagged rather than silently
/// incomplete, per this project's own standing rule. What's implemented: matching against
/// <see cref="ComicVineService.SearchVolumesAsync"/> results (series/volume-level data) and applying
/// the subset of fields available at that level (<see cref="ScrapeField.Publisher"/>,
/// <see cref="ScrapeField.Volume"/>), respecting <see cref="PluginSettings.OverwriteExisting"/>/
/// <see cref="PluginSettings.IgnoreBlankValues"/>/<see cref="PluginSettings.EnabledScrapeFields"/>.
/// What's NOT implemented: matching the specific issue *within* the chosen volume
/// (<see cref="ComicVineService.SearchIssuesAsync"/>/<see cref="ComicVineService.GetIssueDetailsAsync"/>)
/// and applying the remaining CE-verified fields (title, summary, credits, story arc, characters,
/// teams, locations, dates) that only become available once that per-issue lookup exists - real
/// follow-up work, not a gap to guess at filling in here.
/// </summary>
public sealed class ComicVineScrapeOrchestrator
{
    private readonly ComicVineService _comicVine;
    private readonly ComicVineMatchMemory _matchMemory;
    private readonly PluginSettings _settings;

    public ComicVineScrapeOrchestrator(ComicVineService comicVine, ComicVineMatchMemory matchMemory, PluginSettings settings)
    {
        _comicVine = comicVine;
        _matchMemory = matchMemory;
        _settings = settings;
    }

    /// <summary>
    /// <paramref name="interactiveReview"/> is called only when <paramref name="isInteractive"/> is
    /// true and auto-choose is off - shows the ranked candidates and awaits the user's pick (or null
    /// for "skip"). A non-interactive run with auto-choose off skips the book entirely rather than
    /// ever attempting to show a modal (design §4/§9's headless-automation fix - the actual reason
    /// this parameter is nullable).
    /// </summary>
    public async Task<int> ScrapeAsync(
        IReadOnlyList<Issue> books,
        bool isInteractive,
        Func<string, IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>, Task<ComicVineVolumeSearchResult?>>? interactiveReview,
        Func<PaperbunkrDbContext> createDbContext,
        CancellationToken cancellationToken = default)
    {
        int applied = 0;
        foreach (Issue issue in books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? seriesName = issue.Series?.Name;
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                continue;
            }

            IReadOnlyList<ComicVineVolumeSearchResult> candidates;
            try
            {
                candidates = await _comicVine.SearchVolumesAsync(seriesName, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException)
            {
                // A network/API failure for this one book shouldn't abort the batch - same per-item
                // isolation principle as LibraryOrganizerService.ExecuteAsync.
                continue;
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            string searchKey = ComicVineMatchMemory.NormalizeSearchKey(seriesName);
            var ranked = candidates
                .Select(c => (Volume: c, Score: MatchScoreCalculator.Compute(
                    seriesName,
                    issue.EffectiveFormat(),
                    ParseIssueNumber(issue.EffectiveNumber()),
                    issue.EffectiveYear(),
                    c,
                    _matchMemory.WasChosen(searchKey, c.Id),
                    DateTime.UtcNow.Year)))
                .OrderByDescending(c => c.Score)
                .ToList();

            ComicVineVolumeSearchResult? chosen;
            if (_settings.AutoChooseTopMatch)
            {
                chosen = ranked[0].Volume;
            }
            else if (isInteractive && interactiveReview is not null)
            {
                chosen = await interactiveReview(BookLabel(issue), ranked.Select(r => (r.Volume, r.Score)).ToList()).ConfigureAwait(false);
            }
            else
            {
                continue;
            }

            if (chosen is null)
            {
                continue;
            }

            _matchMemory.RecordChoice(searchKey, chosen.Id);
            await ApplyAsync(issue, chosen, createDbContext, cancellationToken).ConfigureAwait(false);
            applied++;
        }

        return applied;
    }

    private async Task ApplyAsync(Issue issue, ComicVineVolumeSearchResult volume, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        using PaperbunkrDbContext context = createDbContext();
        Issue? tracked = await context.Issues.FindAsync(new object[] { issue.Id }, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return;
        }

        if (ShouldWrite(ScrapeField.Publisher, tracked.Publisher, volume.Publisher))
        {
            tracked.Publisher = volume.Publisher;
        }

        if (ShouldWrite(ScrapeField.Volume, tracked.Volume, volume.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            tracked.Volume = volume.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>CE's own overwrite/ignore-blank gate (design doc §4: `ow_existing_b`/`ignore_blanks_b`),
    /// plus this plugin's own enabled-field toggle.</summary>
    private bool ShouldWrite(ScrapeField field, string? currentValue, string? newValue)
    {
        if (!_settings.EnabledScrapeFields.Contains(field))
        {
            return false;
        }

        if (_settings.IgnoreBlankValues && string.IsNullOrEmpty(newValue))
        {
            return false;
        }

        return _settings.OverwriteExisting || string.IsNullOrEmpty(currentValue);
    }

    private static string BookLabel(Issue issue) => $"{issue.Series?.Name} #{issue.EffectiveNumber()}";

    private static int? ParseIssueNumber(string? number) =>
        int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;
}
