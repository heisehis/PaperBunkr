using Avalonia;
using Avalonia.Controls.Primitives;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Activity Center's job-row progress display, extracted into a named reusable control
/// (docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md §3). Deliberately
/// typed directly to <see cref="ActivityJob"/> and scoped to Activity Center's own job rows only -
/// BookDetail/MangaDetail/ReaderChrome/ReadingScreen/Insights/MigrationOverlay keep their own
/// separate ProgressBar usages, since ActivityJob carries real job semantics (Title, a
/// Queued/Running/Succeeded/Failed/Cancelled Status, IsUpkeep) that don't fit a book's reading
/// percentage or a manga chapter's read fraction.
///
/// Code-only <see cref="TemplatedControl"/> (the <c>BrandMark</c>/<c>SplitText</c> pattern -
/// template is an implicit <see cref="Avalonia.Controls.ControlTheme"/> in
/// <c>Styles/Indicators.axaml</c>). This is a pure extraction of
/// <c>ActivityTemplates.axaml</c>'s existing job-row <c>ProgressBar</c> markup - not a visual
/// change.
/// </summary>
public class BusyIndicator : TemplatedControl
{
    public static readonly StyledProperty<ActivityJob?> JobProperty =
        AvaloniaProperty.Register<BusyIndicator, ActivityJob?>(nameof(Job));

    public ActivityJob? Job
    {
        get => GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }
}
