using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Organizing;

/// <summary>One line of the pre-run preview: which book, and where its file goes.</summary>
public sealed record PlanPreviewRow(string Label, string From, string To);

/// <summary>
/// What an <see cref="OrganizePlan"/> would do, in counts and a short sample - shown before a Move so the user confirms something they
/// have actually seen. Simulate mode shows the same thing and then stops.
/// </summary>
public sealed record OrganizePlanSummary(
    int Total,
    int Moving,
    int AlreadyInPlace,
    int Collisions,
    int Problems,
    int Skipped,
    IReadOnlyList<PlanPreviewRow> Sample,
    IReadOnlyList<string> ProblemMessages)
{
    /// <summary>True when there is something to confirm: at least one file would actually move (or collide).</summary>
    public bool HasWork => Moving > 0;

    public static OrganizePlanSummary From(OrganizePlan plan, int sampleSize = 8)
    {
        var actionable = plan.Moves.Where(m => m.Problem is null && m.SkipReason is null && !m.IsAlreadyInPlace).ToList();
        return new OrganizePlanSummary(
            plan.Moves.Count,
            actionable.Count,
            plan.Moves.Count(m => m.IsAlreadyInPlace),
            actionable.Count(m => m.IsCollision),
            plan.Moves.Count(m => m.Problem is not null),
            plan.Moves.Count(m => m.SkipReason is not null),
            actionable.Take(sampleSize).Select(m => new PlanPreviewRow(Describe(m), m.SourcePath, m.DestinationPath)).ToList(),
            plan.Moves.Where(m => m.Problem is not null).Take(5).Select(m => $"{Describe(m)}: {m.Problem}").ToList());
    }

    /// <summary>One summary for a multi-profile run: counts added up, sample rows labelled with the profile they belong to.</summary>
    public static OrganizePlanSummary From(IReadOnlyList<ProfilePlan> plans, int sampleSize = 8)
    {
        if (plans.Count == 1)
        {
            return From(plans[0].Plan, sampleSize);
        }

        var parts = plans.Select(p => (p.Profile, Summary: From(p.Plan, sampleSize))).ToList();
        return new OrganizePlanSummary(
            parts.Sum(p => p.Summary.Total),
            parts.Sum(p => p.Summary.Moving),
            parts.Sum(p => p.Summary.AlreadyInPlace),
            parts.Sum(p => p.Summary.Collisions),
            parts.Sum(p => p.Summary.Problems),
            parts.Sum(p => p.Summary.Skipped),
            parts.SelectMany(p => p.Summary.Sample.Select(r => r with { Label = $"{p.Profile.Name}: {r.Label}" })).Take(sampleSize).ToList(),
            parts.SelectMany(p => p.Summary.ProblemMessages.Select(m => $"{p.Profile.Name}: {m}")).Take(5).ToList());
    }

    public static string Describe(PlannedMove move)
    {
        string label = $"{move.Issue.Series?.Name} #{move.Issue.EffectiveNumber()}".Trim();
        return label.Length > 2 ? label : Path.GetFileName(move.SourcePath);
    }
}

/// <summary>
/// The full text of an organize run: every moved, skipped and failed book with its reason. The Activity Center line can only carry the
/// first few, so the whole list is written to a file (the plugin does the same for Simulate and for failures - "complete log file").
/// </summary>
public static class OrganizeReport
{
    /// <summary>The report of a run that has finished.</summary>
    public static string FromResult(OrganizerProfile profile, OrganizeResult result)
    {
        var text = Header(profile, "Result");
        Section(text, profile.Mode == OrganizerMode.Copy ? "Copied" : "Organized", result.Succeeded.Select(m => $"{m.SourcePath}\n    -> {m.DestinationPath}"));
        Section(text, "Already in place", result.AlreadyInPlace.Select(m => m.SourcePath));
        Section(text, "Skipped", result.Skipped.Select(m => $"{m.SourcePath}{(m.SkipReason is null ? " (an existing file was kept)" : ": " + m.SkipReason)}"));
        Section(text, "Failed", result.Failed.Select(f => $"{f.Move.SourcePath}: {f.Error}"));
        Section(text, "Library entries left without a file (their file was replaced)", result.ReplacedIssueIds.Select(id => $"issue {id}"));
        return text.ToString();
    }

    /// <summary>The report of a Simulate run - what would have happened.</summary>
    public static string FromPlan(OrganizerProfile profile, OrganizePlan plan)
    {
        var text = Header(profile, "Simulation - nothing was moved");
        Section(text, "Would move", plan.Moves.Where(m => m.Problem is null && m.SkipReason is null && !m.IsAlreadyInPlace)
            .Select(m => $"{m.SourcePath}\n    -> {m.DestinationPath}{(m.IsCollision ? "   (a file is already there)" : string.Empty)}"));
        Section(text, "Already in place", plan.Moves.Where(m => m.IsAlreadyInPlace).Select(m => m.SourcePath));
        Section(text, "Would be skipped", plan.Moves.Where(m => m.SkipReason is not null).Select(m => $"{m.SourcePath}: {m.SkipReason}"));
        Section(text, "Would fail", plan.Moves.Where(m => m.Problem is not null).Select(m => $"{m.SourcePath}: {m.Problem}"));
        return text.ToString();
    }

    /// <summary>The report of a finished multi-profile run: one section per profile.</summary>
    public static string FromResults(IReadOnlyList<ProfileResult> results) =>
        results.Count == 1 ? FromResult(results[0].Profile, results[0].Result) : string.Join(Environment.NewLine, results.Select(r => FromResult(r.Profile, r.Result)));

    /// <summary>The report of a multi-profile Simulate - what each profile would have done.</summary>
    public static string FromPlans(IReadOnlyList<ProfilePlan> plans) =>
        plans.Count == 1 ? FromPlan(plans[0].Profile, plans[0].Plan) : string.Join(Environment.NewLine, plans.Select(p => FromPlan(p.Profile, p.Plan)));

    private static StringBuilder Header(OrganizerProfile profile, string title)
    {
        var text = new StringBuilder();
        text.AppendLine($"Organize report - {title}");
        text.AppendLine($"Profile: {profile.Name} ({profile.Mode})  Base folder: {profile.BaseFolder}");
        text.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return text;
    }

    private static void Section(StringBuilder text, string heading, IEnumerable<string> lines)
    {
        var items = lines.ToList();
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine().AppendLine($"{heading} ({items.Count})");
        foreach (string line in items)
        {
            text.AppendLine($"  {line}");
        }
    }

    /// <summary>Writes a report next to the app's other data and returns its path, or null if it could not be written (a report is a
    /// convenience; a full disk must never turn a successful organize into a failure).</summary>
    public static string? TrySave(string reportText, string? folder = null)
    {
        try
        {
            folder ??= Path.Combine(AppDataPaths.Root, "organize-reports");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"organize-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, reportText);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
