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
        Assert.Equal(Enum.GetValues<ScrapeField>().Length, settings.EnabledScrapeFields.Count);
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
