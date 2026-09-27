using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;

namespace Paperbunkr.Data.Tests.Naming;

/// <summary>Template behaviours checked against the real Library Organizer 2.1.13 source (`lobookmover.py`, `locommon.py`) after the
/// audit found the port differing: yes/no polarity, the plugin's own field names inside <c>first()</c>, number padding (decimals,
/// negatives, auto-width) and the multi-value <c>(separator)(issue|series)</c> form. See
/// docs/superpowers/specs/2026-09-25-library-organizer-audit-fixes-design.md.</summary>
public sealed class TemplatePluginParityTests
{
    private static Issue MakeIssue(Action<Issue>? configure = null)
    {
        var issue = new Issue
        {
            Id = 1,
            SeriesId = 1,
            Series = new Series { Id = 1, Name = "Batman" },
            Number = "5",
            Year = 1990,
            Publisher = "DC Comics",
        };
        configure?.Invoke(issue);
        return issue;
    }

    private static string Render(string template, Issue issue, TemplateContext? context = null) => TemplateEvaluator.Evaluate(template, issue, context);

    // -- yes/no fields (`insert_yes_no_field`, lobookmover.py:1546-1581) --

    [Fact]
    public void A_yes_no_token_with_one_argument_shows_its_text_only_for_Yes()
    {
        Issue manga = MakeIssue(i => i.Series!.ContentType = ContentType.Manga);
        Issue comic = MakeIssue(i => i.Series!.ContentType = ContentType.Comic);

        Assert.Equal("Manga", Render("{<manga(Manga)>}", manga));
        Assert.Equal("", Render("{<manga(Manga)>}", comic));
    }

    [Fact]
    public void A_yes_no_token_with_the_bang_argument_shows_its_text_only_for_No()
    {
        Issue manga = MakeIssue(i => i.Series!.ContentType = ContentType.Manga);
        Issue comic = MakeIssue(i => i.Series!.ContentType = ContentType.Comic);

        Assert.Equal("", Render("{<manga(Western)(!)>}", manga));
        Assert.Equal("Western", Render("{<manga(Western)(!)>}", comic));
    }

    // -- first(Field): the plugin's own field names (`insert_first_letter`, lobookmover.py:1612) --

    [Theory]
    [InlineData("{<first(Series)>}", "B")]
    [InlineData("{<first(series)>}", "B")]
    [InlineData("{<first(ShadowSeries)>}", "B")]
    [InlineData("{<first(Publisher)>}", "D")]
    public void First_accepts_the_plugins_field_names_in_any_case(string template, string expected) =>
        Assert.Equal(expected, Render(template, MakeIssue()));

    [Fact]
    public void First_skips_a_leading_article_and_understands_a_two_word_plugin_name()
    {
        Issue issue = MakeIssue(i =>
        {
            i.Series!.Name = "The Amazing Spider-Man";
            i.CoverArtist = "Zeus";
        });

        Assert.Equal("A", Render("{<first(Series)>}", issue));
        Assert.Equal("Z", Render("{<first(Cover Artist)>}", issue));
    }

    [Fact]
    public void First_of_an_unknown_field_is_still_a_loud_error()
    {
        Assert.Throws<NotSupportedException>(() => Render("{<first(Nonsense)>}", MakeIssue()));
    }

    // -- padding (`pad`, lobookmover.py:1973-1998) --

    [Theory]
    [InlineData("7", "{<number3>}", "007")]
    [InlineData("7.5", "{<number3>}", "007.5")]
    [InlineData("-3", "{<number3>}", "-003")]
    [InlineData("1A", "{<number3>}", "1A")]
    [InlineData("7", "{<number>}", "7")]
    public void Numbers_pad_their_whole_part_and_keep_decimals_signs_and_annotations(string number, string template, string expected) =>
        Assert.Equal(expected, Render(template, MakeIssue(i => i.Number = number)));

    [Fact]
    public void A_zero_width_pads_to_the_width_of_the_series_last_issue_number()
    {
        Issue last = MakeIssue(i => i.Number = "120");
        var context = new TemplateContext { Aggregate = new SeriesAggregate(1990, 1, 2000, 1, "1", "120", last) };

        Assert.Equal("005", Render("{<number0>}", MakeIssue(), context));
    }

    [Fact]
    public void A_zero_width_without_a_series_context_leaves_the_number_alone()
    {
        Assert.Equal("5", Render("{<number0>}", MakeIssue()));
    }

    // -- (separator)(issue|series) multi-value form (`insert_multi_value_field`, lobookmover.py:1765-1962) --

    [Fact]
    public void Multi_value_issue_mode_joins_the_issues_own_values()
    {
        Issue issue = MakeIssue(i => i.Writer = "Alan Moore, Dave Gibbons");

        Assert.Equal("Alan Moore & Dave Gibbons", Render("{<writer( & )(issue)>}", issue));
    }

    [Fact]
    public void Multi_value_series_mode_joins_every_issues_values_once_and_gives_the_same_text_to_every_issue()
    {
        Issue first = MakeIssue(i => { i.Id = 1; i.Writer = "Alan Moore, Dave Gibbons"; });
        Issue second = MakeIssue(i => { i.Id = 2; i.Writer = "Dave Gibbons, Len Wein"; });
        var context = new TemplateContext { SeriesBooks = new[] { first, second } };

        Assert.Equal("Alan Moore; Dave Gibbons; Len Wein", Render("{<writer(; )(series)>}", first, context));
        Assert.Equal("Alan Moore; Dave Gibbons; Len Wein", Render("{<writer(; )(series)>}", second, context));
    }

    [Fact]
    public void Multi_value_works_on_tags_and_genres_too()
    {
        Issue issue = MakeIssue(i =>
        {
            i.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Superhero" });
            i.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Crime" });
        });

        Assert.Equal("Superhero, Crime", Render("{<genre(, )(issue)>}", issue));
    }

    [Fact]
    public void Multi_value_with_an_unknown_mode_is_a_loud_error()
    {
        Assert.Throws<NotSupportedException>(() => Render("{<writer(, )(everything)>}", MakeIssue(i => i.Writer = "A")));
    }

    // -- EmptyData / empty tracking (`insert_field`, lobookmover.py:1432-1442) --

    [Fact]
    public void An_empty_token_uses_the_profiles_fallback_text_without_the_groups_prefix_or_postfix()
    {
        var context = new TemplateContext { EmptyData = new Dictionary<string, string> { ["publisher"] = "Unknown" } };
        Issue issue = MakeIssue(i => i.Publisher = null);

        Assert.Equal("Unknown", Render("{[<publisher>]}", issue, context));
        Assert.Equal(new[] { "publisher" }, context.EmptyTokens);
    }

    [Fact]
    public void A_token_that_resolves_is_not_recorded_as_empty()
    {
        var context = new TemplateContext();

        Render("{<series>}{<publisher>}", MakeIssue(), context);

        Assert.Empty(context.EmptyTokens);
    }
}
