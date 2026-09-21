using Paperbunkr.Data.Naming;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.Naming;

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

    private static string Render(string template, Issue issue, TemplateContext? context = null) =>
        TemplateEvaluator.Evaluate(template, issue, context);

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
    public void Conditional_group_with_a_text_arg_emits_wrapper_only_on_exact_match()
    {
        // Real CE semantics (lobookmover.py:1394, verified): a Conditional group's LAST paren segment
        // is a comparison value, not a field arg - it never emits the resolved value itself, only the
        // prefix+postfix, and only when the field's value equals that comparison text exactly.
        Issue tpb = MakeIssue(i => i.Format = "TPB");
        Issue notTpb = MakeIssue(i => i.Format = "Single Issue");

        Assert.Equal("[TPB]", Render("{[TPB]<?format(TPB)>}", tpb));
        Assert.Equal("", Render("{[TPB]<?format(TPB)>}", notTpb));
    }

    [Fact]
    public void Conditional_group_with_a_regex_arg_emits_wrapper_only_on_a_leading_match()
    {
        // A `!`-prefixed condition arg is a regex, matched with Python re.match semantics - anchored at
        // the start, not required to consume the whole string (lobookmover.py:1396-1403, verified).
        Issue oneShot = MakeIssue(i => i.Number = "1");
        Issue notOneShot = MakeIssue(i => i.Number = "12");

        Assert.Equal("(one-shot)", Render("{(one-shot)<?number(!1$)>}", oneShot));
        Assert.Equal("", Render("{(one-shot)<?number(!1$)>}", notOneShot));
    }

    [Fact]
    public void Conditional_group_with_a_bare_regex_bang_always_resolves_empty()
    {
        // CE's own special case: `(!)` with nothing after the `!` always resolves to nothing at all,
        // regardless of the field's value (lobookmover.py:1397-1398, verified).
        Assert.Equal("", Render("{X<?format(!)>}", MakeIssue()));
    }

    [Fact]
    public void Conditional_group_with_no_trailing_arg_throws()
    {
        // CE leaves the raw template text in place for this malformed case; Paperbunkr throws instead,
        // matching this project's own deliberate "loud error over silent passthrough" deviation.
        Assert.Throws<NotSupportedException>(() => Render("{X<?format>}", MakeIssue()));
    }

    [Fact]
    public void Inversion_group_with_no_args_emits_wrapper_text_only_when_the_field_is_empty()
    {
        Issue withVolume = MakeIssue();
        Issue withoutVolume = MakeIssue(i => i.Volume = null);

        Assert.Equal("", Render("{No volume<!volume>}", withVolume));
        Assert.Equal("No volume", Render("{No volume<!volume>}", withoutVolume));
    }

    [Fact]
    public void Inversion_group_with_a_text_arg_emits_wrapper_when_the_value_does_not_equal_it()
    {
        // lobookmover.py:1426-1431, verified - the opposite polarity from Conditional's text-arg form.
        Issue tpb = MakeIssue(i => i.Format = "TPB");
        Issue notTpb = MakeIssue(i => i.Format = "Single Issue");

        Assert.Equal("", Render("{Not a TPB<!format(TPB)>}", tpb));
        Assert.Equal("Not a TPB", Render("{Not a TPB<!format(TPB)>}", notTpb));
    }

    [Fact]
    public void Inversion_group_with_a_regex_arg_emits_wrapper_when_there_is_no_leading_match()
    {
        // lobookmover.py:1420-1425, verified - opposite polarity from Conditional's regex-arg form.
        Issue oneShot = MakeIssue(i => i.Number = "1");
        Issue notOneShot = MakeIssue(i => i.Number = "12");

        Assert.Equal("", Render("{Not #1<!number(!1$)>}", oneShot));
        Assert.Equal("Not #1", Render("{Not #1<!number(!1$)>}", notOneShot));
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
        Assert.Throws<NotSupportedException>(() => Render("{<totallyMadeUpToken>}", MakeIssue()));
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

        // "month" (unpadded) is CE's LOCALIZED MONTH NAME token, not a number (lobookmover.py:1521,
        // verified) - a prior pass had this backwards. "April", not "4".
        Assert.Equal("Batman Vol.2 #07 (of 12) (April, 1990)", rendered);
        Assert.DoesNotContain('{', rendered);
        Assert.DoesNotContain('}', rendered);
    }

    // -- Newly closed token gaps (real Issue fields a prior pass's doc comment wrongly claimed didn't exist) --

    [Fact]
    public void AltCount_token_resolves_the_real_AlternateCount_field()
    {
        Issue issue = MakeIssue(i => i.AlternateCount = 3);
        Assert.Equal("3", Render("{<altCount>}", issue));
    }

    [Fact]
    public void Released_and_added_date_tokens_resolve_the_real_DateTime_fields_as_ISO_dates()
    {
        Issue issue = MakeIssue(i =>
        {
            i.ReleasedTime = new DateTime(1990, 4, 15);
            i.AddedTime = new DateTime(2026, 1, 2);
        });

        Assert.Equal("1990-04-15", Render("{<ReleasedDate>}", issue));
        Assert.Equal("2026-01-02", Render("{<AddedDate>}", issue));
    }

    [Fact]
    public void Month_hash_token_is_the_plain_number_and_only_pads_with_an_explicit_width_arg()
    {
        // CE's insert_text_field (lobookmover.py:1514-1529, verified) doesn't auto-pad "month#" with no
        // args at all - only a bare digit-width arg (`<month#2>`, this project's own `<numberN>`
        // convention) pads it, same as every other numeric token.
        Issue issue = MakeIssue(i => i.Month = 4);
        Assert.Equal("4", Render("{<month#>}", issue));
        Assert.Equal("04", Render("{<month#2>}", issue));
        Assert.Equal("12", Render("{<month#>}", MakeIssue(i => i.Month = 12)));
    }

    [Fact]
    public void First_token_takes_the_first_character_of_another_field_resolved_recursively()
    {
        Assert.Equal("B", Render("{<first(series)>}", MakeIssue()));
        Assert.Equal("", Render("{<first(series)>}", MakeIssue(i => i.Series!.Name = "")));
    }

    [Fact]
    public void First_token_strips_a_leading_article_before_taking_the_letter()
    {
        // CE's exact article list (lobookmover.py:1625, verified) - English "The"/"A"/"An" plus several
        // other languages' articles are stripped before the first letter is taken, then capitalized.
        Assert.Equal("A", Render("{<first(series)>}", MakeIssue(i => i.Series!.Name = "The Amazing Spider-Man")));
        Assert.Equal("F", Render("{<first(series)>}", MakeIssue(i => i.Series!.Name = "la Femme")));
        // No article present - just the plain first letter, capitalized.
        Assert.Equal("B", Render("{<first(series)>}", MakeIssue(i => i.Series!.Name = "batman")));
    }

    [Fact]
    public void First_token_with_an_unsupported_inner_field_throws()
    {
        Assert.Throws<NotSupportedException>(() => Render("{<first(totallyMadeUpToken)>}", MakeIssue()));
    }

    // -- CE's real ReadPercentage ("read") and running Counter tokens (lobookmover.py, verified) --

    [Fact]
    public void Read_token_emits_its_text_only_when_the_operator_comparison_matches()
    {
        Issue halfRead = MakeIssue(i => { i.PageCount = 100; i.LastPageRead = 50; });

        Assert.Equal("Half read", Render("{<read(Half read)(=)(50)>}", halfRead));
        Assert.Equal("", Render("{<read(Half read)(=)(51)>}", halfRead));
        Assert.Equal("Mostly read", Render("{<read(Mostly read)(>)(40)>}", halfRead));
        Assert.Equal("Barely read", Render("{<read(Barely read)(<)(60)>}", halfRead));
    }

    [Fact]
    public void Read_token_never_matches_when_page_count_is_unknown()
    {
        Issue unknown = MakeIssue(i => { i.PageCount = null; i.LastPageRead = 50; });
        Assert.Equal("", Render("{<read(Any)(>)(0)>}", unknown));
    }

    [Fact]
    public void Counter_token_seeds_at_start_then_advances_by_increment_padded()
    {
        var context = new TemplateContext();
        Issue issue = MakeIssue();

        Assert.Equal("001", Render("{<counter(1)(1)(3)>}", issue, context));
        Assert.Equal("002", Render("{<counter(1)(1)(3)>}", issue, context));
        Assert.Equal("012", Render("{<counter(1)(10)(3)>}", issue, context));
    }

    [Fact]
    public void Counter_token_with_no_context_resolves_empty_rather_than_fabricating_one()
    {
        Assert.Equal("", Render("{<counter(1)(1)(3)>}", MakeIssue()));
    }

    [Fact]
    public void Month_token_resolves_the_localized_month_name_while_month_hash_stays_numeric()
    {
        Issue issue = MakeIssue(i => i.Month = 4);
        Assert.Equal("April", Render("{<month>}", issue));
        Assert.Equal("4", Render("{<month#>}", issue));
    }

    [Fact]
    public void Month_token_uses_the_contexts_own_month_names_when_supplied()
    {
        var context = new TemplateContext { MonthNames = new Dictionary<int, string> { [4] = "Apr" } };
        Issue issue = MakeIssue(i => i.Month = 4);
        Assert.Equal("Apr", Render("{<month>}", issue, context));
    }

    // -- Series-level aggregate tokens (SeriesAggregate) --

    [Fact]
    public void Aggregate_tokens_resolve_to_empty_with_no_context_supplied()
    {
        // No SeriesAggregate ever exists without going through LibraryOrganizerService.PlanAsync (a
        // raw TemplateEvaluator.Evaluate call, as most of this test file makes, has no such lookup) -
        // every aggregate-backed token simply resolves empty rather than guessing at a fallback CE
        // itself doesn't have (get_earliest_book/get_last_book always run for real usage).
        Issue issue = MakeIssue();
        Assert.Equal("", Render("{<startyear>}", issue));
        Assert.Equal("", Render("{<EndYear>}", issue));
        Assert.Equal("", Render("{<startmonth>}", issue));
        Assert.Equal("", Render("{<firstissuenumber>}", issue));
    }

    [Fact]
    public void Aggregate_tokens_resolve_from_a_supplied_SeriesAggregate()
    {
        var aggregate = new SeriesAggregate(StartYear: 1940, StartMonth: 3, EndYear: 2011, EndMonth: 9, FirstIssueNumber: "1", LastIssueNumber: "897");
        var context = new TemplateContext { Aggregate = aggregate };
        Issue issue = MakeIssue();

        Assert.Equal("1940", Render("{<startyear>}", issue, context));
        Assert.Equal("March", Render("{<startmonth>}", issue, context));
        Assert.Equal("3", Render("{<startmonth#>}", issue, context));
        Assert.Equal("03", Render("{<startmonth#2>}", issue, context));
        Assert.Equal("2011", Render("{<EndYear>}", issue, context));
        Assert.Equal("September", Render("{<EndMonth>}", issue, context));
        Assert.Equal("9", Render("{<EndMonth#>}", issue, context));
        Assert.Equal("09", Render("{<EndMonth#2>}", issue, context));
        Assert.Equal("1", Render("{<firstissuenumber>}", issue, context));
        Assert.Equal("897", Render("{<lastissuenumber>}", issue, context));
    }
}
