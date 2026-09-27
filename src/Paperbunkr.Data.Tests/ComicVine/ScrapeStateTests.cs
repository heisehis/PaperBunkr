using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Tests.Acquisition;

namespace Paperbunkr.Data.Tests.ComicVine;

public class ScrapeStateTests : AcquisitionTestBase
{
    [Fact]
    public void Settings_DefaultToCeParity_WhenNothingIsSaved()
    {
        var settings = ScrapeSettings.Load(Context);

        Assert.True(settings.OverwriteExisting);
        Assert.False(settings.AutoChooseTopMatch);
        Assert.True(settings.ConfirmIssueMatch);
        Assert.Equal(100, settings.MaxSearchResults);
        // CE's update_rating_b defaults off (configuration.py:62, verified) - docs/superpowers/specs/
        // 2026-09-24-comicvine-scraper-fidelity-design.md §1.3. Every other field defaults on.
        Assert.Equal(Enum.GetValues<ScrapeField>().Length - 1, settings.EnabledScrapeFields.Count);
        Assert.DoesNotContain(ScrapeField.CommunityRating, settings.EnabledScrapeFields);
    }

    [Fact]
    public void Settings_RoundTrip_IncludingTheSetsAndDictionaries()
    {
        var saved = new ScrapeSettings
        {
            AutoChooseTopMatch = true,
            IgnoreBlankValues = true,
            EnabledScrapeFields = new HashSet<ScrapeField> { ScrapeField.Title, ScrapeField.Summary },
            ImprintOverrides = new Dictionary<string, string> { ["Vertigo"] = "DC Comics" },
            IgnoredPublishers = new HashSet<string> { "Panini" },
            IgnoredSearchTerms = new HashSet<string> { "annual" },
            IgnoreVolumesBeforeYear = 1990,
        };
        saved.Save(Context);
        saved.Save(Context);                                          // saving twice updates the one row

        var loaded = ScrapeSettings.Load(NewContext());

        Assert.True(loaded.AutoChooseTopMatch);
        Assert.Equal(new[] { ScrapeField.Title, ScrapeField.Summary }, loaded.EnabledScrapeFields.OrderBy(f => f));
        Assert.Equal("DC Comics", loaded.ImprintOverrides["Vertigo"]);
        Assert.Contains("Panini", loaded.IgnoredPublishers);
        Assert.Equal(1990, loaded.IgnoreVolumesBeforeYear);
        Assert.Single(Context.ScrapeSettingsRows);
    }

    [Fact]
    public void NewPhase3Settings_DefaultToCeParity_AndRoundTrip()
    {
        var defaults = ScrapeSettings.Load(Context);
        Assert.True(defaults.ConvertImprints);
        Assert.True(defaults.ForceSeriesArt);
        Assert.True(defaults.ShowCovers);
        Assert.Equal(1000, defaults.ScrapeDelayMs);
        Assert.Empty(defaults.PublisherAliases);

        var saved = new ScrapeSettings
        {
            ConvertImprints = false,
            ForceSeriesArt = false,
            ShowCovers = false,
            ScrapeDelayMs = 5000,
            PublisherAliases = new Dictionary<string, string> { ["Marvel UK"] = "Marvel" },
        };
        saved.Save(Context);

        var loaded = ScrapeSettings.Load(NewContext());
        Assert.False(loaded.ConvertImprints);
        Assert.False(loaded.ForceSeriesArt);
        Assert.False(loaded.ShowCovers);
        Assert.Equal(5000, loaded.ScrapeDelayMs);
        Assert.Equal("Marvel", loaded.PublisherAliases["Marvel UK"]);
    }

    [Fact]
    public void ARowSavedBeforeTheForkFieldsExisted_GetsThemTurnedOnOnce_ThenRespectsALaterOptOut()
    {
        // A saved set only lists the fields that existed when it was saved - without a one-time bump the
        // five fork fields would stay silently off for every existing user.
        Context.ScrapeSettingsRows.Add(new ScrapeSettingsRow
        {
            Id = 1,
            Json = """{"EnabledScrapeFields":[0,4],"OverwriteExisting":true}""",   // Series, Title: an old-style row with no FieldSetVersion
        });
        Context.SaveChanges();

        var migrated = ScrapeSettings.Load(NewContext());
        Assert.Contains(ScrapeField.Count, migrated.EnabledScrapeFields);
        Assert.Contains(ScrapeField.StoryArcOrder, migrated.EnabledScrapeFields);
        Assert.Contains(ScrapeField.Title, migrated.EnabledScrapeFields);
        Assert.DoesNotContain(ScrapeField.Summary, migrated.EnabledScrapeFields);   // an old field the user had off stays off

        migrated.EnabledScrapeFields.Remove(ScrapeField.Count);                      // the user opts out of one
        migrated.Save(Context);

        Assert.DoesNotContain(ScrapeField.Count, ScrapeSettings.Load(NewContext()).EnabledScrapeFields);
    }

    [Fact]
    public void ACorruptSettingsRow_FallsBackToDefaults_InsteadOfBlockingScraping()
    {
        Context.ScrapeSettingsRows.Add(new ScrapeSettingsRow { Id = 1, Json = "{not json" });
        Context.SaveChanges();

        Assert.True(ScrapeSettings.Load(NewContext()).ConfirmIssueMatch);
    }

    [Fact]
    public void MatchMemory_RemembersAChoice_Idempotently_AndKeysOnTheNormalizedName()
    {
        var memory = new ComicVineMatchMemory(NewContext);
        var key = ComicVineMatchMemory.NormalizeSearchKey("  The   Amazing SPIDER-Man ");

        Assert.Equal("the amazing spider-man", key);
        Assert.False(memory.WasChosen(key, 42));

        memory.RecordChoice(key, 42);
        memory.RecordChoice(key, 42);

        Assert.True(memory.WasChosen(key, 42));
        Assert.False(memory.WasChosen(key, 43));
        Assert.Single(Context.ComicVineMatchMemories);
    }
}
