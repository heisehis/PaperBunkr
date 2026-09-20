namespace Paperbunkr.App.Scraper;

/// <summary>
/// The shell's scrape and organize coordinators, reachable from the static scheduled-task catalog (whose lambdas have no other way to the app's services).
/// Set once by <c>MainViewModel</c>; a task that runs before that (or in a test with no shell) reports that instead of guessing.
/// </summary>
public static class ScheduledCoordinators
{
    public static ScrapeCoordinator? Scraper { get; set; }

    public static OrganizeCoordinator? Organizer { get; set; }
}
