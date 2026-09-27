using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Naming;

/// <summary>
/// Checks a naming template when a profile is saved, so a mistake shows up in the editor rather than as a failed book on the next run.
/// Evaluates the template against a made-up issue: an unknown token, a malformed multi-value argument or a stray brace is found, not just
/// bad syntax. (The plugin leaves such text in the path as it is - `lobookmover.py:1380` - which mangles the folder name; Paperbunkr
/// tells you instead.)
/// </summary>
public static class TemplateValidator
{
    private static Issue SampleIssue() => new()
    {
        Id = 1,
        SeriesId = 1,
        Series = new Series { Id = 1, Name = "Batman" },
        Number = "12",
        Volume = "2",
        Year = 2011,
        Month = 5,
        Publisher = "DC Comics",
        Imprint = "Vertigo",
        Format = "Annual",
        Title = "Sample",
    };

    /// <summary>The template's problem, or null when it can be used. <paramref name="allowEmpty"/> is true for the folder template, where
    /// an empty template simply means "no subfolders".</summary>
    public static string? Validate(string? template, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return allowEmpty ? null : "The file template can't be empty.";
        }

        try
        {
            Issue sample = SampleIssue();
            var context = new TemplateContext { Aggregate = new SeriesAggregate(2011, 1, 2014, 12, "1", "40", sample, new[] { sample }) };
            string text = TemplateEvaluator.Evaluate(template, sample, context);
            return text.Contains('{') || text.Contains('}')
                ? "The template has an unbalanced { or } (a token looks like {<series>})."
                : null;
        }
        catch (NotSupportedException ex)
        {
            return ex.Message;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
