using ClusterLibraryManager.Templating;
using Paperbunkr.Data.Entities;

namespace ClusterLibraryManager.Tests;

/// <summary>Implementation plan Phase 3 Step 3.3 verification - one test per token type (scalar,
/// padded numeric, multi-value+separator, conditional group, inversion group), plus the full default
/// template end to end against a fixture <see cref="Issue"/>.</summary>
public sealed class TemplateEngineTests
{
    private static Issue MakeIssue(Action<Issue>? configure = null)
    {
        var series = new Series { Id = 1, Name = "Batman" };
        var issue = new Issue
        {
            Id = 1,
            SeriesId = 1,
            Series = series,
            Number = "1",
            Volume = "2",
            Year = 1990,
            Publisher = "DC Comics",
            Format = "TPB",
        };
        configure?.Invoke(issue);
        return issue;
    }

    private static string Render(string template, Issue issue) =>
        TemplateEvaluator.Evaluate(template, issue);

    [Fact]
    public void Scalar_token_resolves_the_effective_field()
    {
        Assert.Equal("Batman", Render("{<series>}", MakeIssue()));
        Assert.Equal("1990", Render("{<year>}", MakeIssue()));
    }

    [Fact]
    public void A_group_collapses_entirely_including_its_prefix_and_postfix_when_the_field_is_empty()
    {
        Issue issue = MakeIssue(i => i.Volume = null);
        Assert.Equal("", Render("{ Vol.<volume>}", issue));
        Assert.Equal("Batman", Render("{<series>}{ Vol.<volume>}", issue));
    }

    [Fact]
    public void A_group_emits_its_prefix_and_postfix_only_when_the_field_is_present()
    {
        Assert.Equal(" Vol.2", Render("{ Vol.<volume>}", MakeIssue()));
    }

    [Fact]
    public void Padded_numeric_token_left_pads_to_the_requested_width()
    {
        Issue issue = MakeIssue(i => i.Number = "7");
        Assert.Equal(" #007", Render("{ #<number3>}", issue));
    }

    [Fact]
    public void Padded_numeric_token_leaves_a_non_numeric_issue_number_unchanged()
    {
        Issue issue = MakeIssue(i => i.Number = "1A");
        Assert.Equal(" #1A", Render("{ #<number3>}", issue));
    }

    [Fact]
    public void Multivalue_token_joins_tags_with_the_given_separator()
    {
        Issue issue = MakeIssue(i => i.Tags.AddRange(new[]
        {
            new IssueTag { Field = IssueTagField.Genre, Value = "Superhero" },
            new IssueTag { Field = IssueTagField.Genre, Value = "Action" },
            new IssueTag { Field = IssueTagField.Tags, Value = "Ignored" },
        }));

        Assert.Equal("Superhero; Action", Render("{<genre(; )>}", issue));
    }

    [Fact]
    public void Multivalue_token_defaults_to_a_comma_space_separator_when_none_given()
    {
        Issue issue = MakeIssue(i => i.Tags.AddRange(new[]
        {
            new IssueTag { Field = IssueTagField.Tags, Value = "A" },
            new IssueTag { Field = IssueTagField.Tags, Value = "B" },
        }));

        Assert.Equal("A, B", Render("{<tags>}", issue));
    }

    [Fact]
    public void Custom_token_looks_up_a_custom_value_by_name()
    {
        Issue issue = MakeIssue(i => i.CustomValues.Add(new IssueCustomValue { Name = "EditionNote", Value = "Deluxe" }));

        Assert.Equal("Deluxe", Render("{<Custom(EditionNote)>}", issue));
    }

    [Fact]
    public void Conditional_group_behaves_like_normal_collapse_on_empty_in_this_pass()
    {
        Assert.Equal("2", Render("{<?volume>}", MakeIssue()));
        Assert.Equal("", Render("{<?volume>}", MakeIssue(i => i.Volume = null)));
    }

    [Fact]
    public void Inversion_group_emits_wrapper_text_only_when_the_field_is_empty()
    {
        Issue withVolume = MakeIssue();
        Issue withoutVolume = MakeIssue(i => i.Volume = null);

        Assert.Equal("", Render("{No volume<!volume>}", withVolume));
        Assert.Equal("No volume", Render("{No volume<!volume>}", withoutVolume));
    }

    [Fact]
    public void Yes_no_token_uses_the_given_true_false_text()
    {
        Issue manga = MakeIssue(i => i.Series!.ContentType = ContentType.Manga);
        Issue comic = MakeIssue(i => i.Series!.ContentType = ContentType.Comic);

        Assert.Equal("Manga", Render("{<manga(Manga)(!)>}", manga));
        // Only one paren segment given -> false text defaults to "No".
        Assert.Equal("No", Render("{<manga(Manga)>}", comic));
    }

    [Fact]
    public void Unsupported_token_throws_rather_than_silently_resolving_to_empty()
    {
        Assert.Throws<NotSupportedException>(() => Render("{<counter>}", MakeIssue()));
    }

    [Fact]
    public void Default_template_renders_end_to_end_against_a_fixture_issue()
    {
        // CE's own fallback default (design doc §5, losettings.py:396-397).
        const string fileTemplate = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";
        Issue issue = MakeIssue(i =>
        {
            i.Number = "7";
            i.Count = 12;
            i.Year = 1990;
        });

        string rendered = Render(fileTemplate, issue);

        Assert.Equal("Batman Vol.2 #07 (of 12) (1990)", rendered);
    }

    [Fact]
    public void Default_template_includes_the_month_when_known_proving_multipass_nesting_resolution()
    {
        // This is the case that actually exercises the apparent-nesting behavior in
        // TemplateEvaluator's own doc comment: {<month>, } is a real inner group inside the outer
        // { (...)} wrapper. A naive single-pass evaluator would leave stray "{"/"}" characters in the
        // output here instead of resolving the outer group on a second pass.
        const string fileTemplate = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";
        Issue issue = MakeIssue(i =>
        {
            i.Number = "7";
            i.Count = 12;
            i.Year = 1990;
            i.Month = 4;
        });

        string rendered = Render(fileTemplate, issue);

        Assert.Equal("Batman Vol.2 #07 (of 12) (4, 1990)", rendered);
        Assert.DoesNotContain('{', rendered);
        Assert.DoesNotContain('}', rendered);
    }
}
