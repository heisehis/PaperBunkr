using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BusyIndicator"/> (docs/superpowers/specs/2026-09-06-feedback-notification-system-
/// design.md §3 / plan Step 4). The control itself has no computed logic - it just forwards the
/// bound <see cref="ActivityJob"/> to its template's ProgressBar via
/// Job.Fraction/Job.IsIndeterminate, which <see cref="ActivityJob"/>'s own existing tests already
/// cover. This is a smoke test that the property round-trips correctly; the actual template
/// rendering needs App.axaml styles loaded (not available headless, same gap BrandMark's tests
/// document) so it's covered by manual GUI verification instead (plan Step 20).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BusyIndicatorTests
{
    [Fact]
    public void Job_RoundTrips()
    {
        var job = new ActivityJob { Kind = ActivityJobKind.Import, Title = "Importing" };
        var indicator = new BusyIndicator { Job = job };

        Assert.Same(job, indicator.Job);
    }

    [Fact]
    public void NoJob_DoesNotThrow()
    {
        var indicator = new BusyIndicator();

        Assert.Null(indicator.Job);
    }
}
