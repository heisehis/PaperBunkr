// Franchise Tools - Library hook (Plugin API v3: IMetadataGraph + IMetadataWriter, confirmWrites
// gate). Right-click any issue -> tags every issue across every series in its whole connected
// "series family" - direct relations (sequel/spinoff/crossover/...) plus shared continuities,
// transitively unioned by SeriesFamilyResolver via Environment.Metadata.GetSeriesFamily. Neither
// CE nor Paperbunkr's own UI has a one-click "tag the whole franchise" action anywhere today.
var book = Books.First();
var family = Environment.Metadata.GetSeriesFamily(new Series { Id = book.SeriesId });
var seriesIds = new HashSet<int>(family.Select(s => s.Id)) { book.SeriesId };

var toTag = Environment.App.GetLibraryBooks().Where(i => seriesIds.Contains(i.SeriesId)).ToList();

Environment.App.AskQuestion(
    $"Tag {toTag.Count} issue(s) across {seriesIds.Count} connected series?",
    "Yes",
    "No");

int tagged = 0;
foreach (var issue in toTag)
{
    if (Environment.Writer.AddTag(issue, "franchise"))
    {
        tagged++;
    }
}

return tagged;
