using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Plugin API 4.5 host pieces (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md): smart field storage and its
/// Smart List editor entry (§D1), the provider registry's timeout and error guard (§C), and <c>Environment.Http</c>'s host check (§F).
/// Real SQLite and real manifests; the HTTP client is a stub handler, so nothing leaves the machine. The field registry is process-wide:
/// every test restores it.
/// </summary>
public sealed class PluginApi45Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_api45_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly IReadOnlyList<PluginSmartField> _savedFields = PluginFieldTypeRegistry.All;
    private readonly PluginLogFiles _logs;

    public PluginApi45Tests()
    {
        Directory.CreateDirectory(_root);
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "t.db")}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        _logs = new PluginLogFiles(() => Path.Combine(_root, "logs"));
    }

    public void Dispose()
    {
        PluginFieldTypeRegistry.Replace(_savedFields);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private int SeedIssue()
    {
        using var context = NewContext();
        var issue = new Issue { Series = new Series { Name = "Kilo Station" }, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static PluginFieldSchema Schema(string xml)
    {
        string file = Path.Combine(Path.GetTempPath(), $"m-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, $"""<Plugin key="tracker" name="Tracker">{xml}</Plugin>""");
        try
        {
            return PluginFieldSchema.Parse("tracker", XmlPluginInitializer.ReadManifest(file)!, out _)!;
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ================================================================ §D1 fields

    [Fact]
    public void Fields_store_normalised_typed_values_as_namespaced_custom_values()
    {
        int id = SeedIssue();
        var log = new PluginLogger(_logs, "tracker");
        var fields = new PluginFieldsAdapter(NewContext, Schema("""<Fields><Field key="days" type="number"/><Field key="seen" type="date"/></Fields>"""), "tracker", log);
        var issue = new Issue { Id = id };

        Assert.True(fields.Set(issue, "days", " 12.50 "));
        Assert.True(fields.Set(issue, "seen", "2026-10-28T10:00:00Z"));
        Assert.False(fields.Set(issue, "days", "twelve"));      // not a number
        Assert.False(fields.Set(issue, "undeclared", "1"));     // not in <Fields>
        Assert.False(fields.Set(new Issue { Id = 99999 }, "days", "1"));

        Assert.Equal("12.5", fields.Get(issue, "days"));
        Assert.Equal("2026-10-28", fields.Get(issue, "seen"));
        using (var context = NewContext())
        {
            Assert.Equal(new[] { "tracker.days", "tracker.seen" },
                context.Set<IssueCustomValue>().Where(cv => cv.IssueId == id).Select(cv => cv.Name).OrderBy(n => n).ToArray());
        }

        string logged = File.ReadAllText(_logs.PathFor("tracker"));
        Assert.Contains("isn't a valid number", logged);
        Assert.Contains("isn't declared", logged);

        Assert.True(fields.Set(issue, "days", null));
        Assert.Null(fields.Get(issue, "days"));
        Assert.Equal(1, fields.Clear("seen"));
    }

    [Fact]
    public void The_smart_list_editor_lists_plugin_fields_with_their_types_operators()
    {
        PluginFieldTypeRegistry.Replace(new[] { new PluginSmartField("tracker.days", "Days since read (Tracker)", SmartListDataType.Number) });
        var condition = new SmartListCondition { Field = SmartListField.Title, Operator = SmartListOperator.Contains, Value = "x" };
        var row = new SmartListConditionViewModel(condition, SmartListTargetKind.Issue, _ => { }, () => { });

        FieldOption option = Assert.Single(row.FieldOptions, o => o.CustomValueName == "tracker.days");
        Assert.Equal("Days since read (Tracker)", option.Label);
        Assert.Contains(row.FieldOptions, o => o.Field == SmartListField.CustomValue && o.CustomValueName is null);   // the plain one is still there

        row.SelectedFieldText = "Days since read (Tracker)";

        Assert.Equal(SmartListField.CustomValue, condition.Field);
        Assert.Equal("tracker.days", condition.CustomValueName);
        Assert.True(row.IsPluginField);
        Assert.False(row.IsCustomValueField);             // no name box for a plugin field
        Assert.False(row.ShowCaseToggle);                 // numbers have no case
        Assert.Contains(row.OperatorOptions, o => o.Operator == SmartListOperator.GreaterThan);
        Assert.Equal("Days since read (Tracker)", row.SelectedFieldText);

        row.SelectedFieldText = "Custom Value";

        Assert.Null(condition.CustomValueName);
        Assert.True(row.IsCustomValueField);
    }

    [Fact]
    public void A_condition_on_a_removed_plugins_field_reads_as_a_plain_custom_value()
    {
        PluginFieldTypeRegistry.Replace(Array.Empty<PluginSmartField>());
        var condition = new SmartListCondition { Field = SmartListField.CustomValue, CustomValueName = "gone.days", Operator = SmartListOperator.Is, Value = "1" };
        var row = new SmartListConditionViewModel(condition, SmartListTargetKind.Issue, _ => { }, () => { });

        Assert.False(row.IsPluginField);
        Assert.True(row.IsCustomValueField);
        Assert.Equal("gone.days", row.CustomValueName);
    }

    [Fact]
    public void The_host_publishes_live_plugins_fields_and_drops_disabled_ones()
    {
        string plugins = Path.Combine(_root, "plugins");
        string dir = Path.Combine(plugins, "tracker");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """
            <Plugin key="tracker" name="Tracker">
              <Command hook="Startup" key="tracker.s" name="s" script="s.csx" />
              <Fields><Field key="days" label="Days since read" type="number"/></Fields>
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "s.csx"), "return 1;");
        var host = new PluginHostService { PluginsRoot = () => plugins, ContextFactory = NewContext, LogFiles = _logs, Providers = new PluginProviderRegistry(_logs) };

        host.InitializeForTests(new TestPluginEnvironment());

        Assert.True(PluginFieldTypeRegistry.TryGet("tracker.days", out var field));
        Assert.Equal("Days since read (Tracker)", field.Label);

        host.SetPackageEnabled("tracker", false);

        Assert.False(PluginFieldTypeRegistry.TryGet("tracker.days", out _));
    }

    // ================================================================ §D2 write batches

    private PaperbunkrMetadataWriter Writer(int answer, List<ConfirmDialogRequest> shown) => new()
    {
        CreateContext = NewContext,
        ShowDialog = request =>
        {
            shown.Add(request);
            return Task.FromResult(answer);
        },
    };

    private (string? Format, string? BookAge, string[] Tags) Read(int id)
    {
        using var context = NewContext();
        var issue = context.Issues.Include(i => i.Tags).Single(i => i.Id == id);
        return (issue.Format, issue.BookAge, issue.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).OrderBy(v => v).ToArray());
    }

    [Fact]
    public async Task A_batch_previews_every_change_and_applies_them_together()
    {
        int a = SeedIssue();
        int b = SeedIssue();
        var shown = new List<ConfirmDialogRequest>();
        var batch = Writer(answer: 0, shown).BeginBatch();

        Assert.True(batch.SetFormat(new Issue { Id = a }, "Trade Paperback"));
        Assert.True(batch.AddTag(new Issue { Id = a }, "keeper"));
        Assert.True(batch.SetBookAge(new Issue { Id = b }, "Modern"));
        Assert.False(batch.SetFormat(new Issue { Id = 99999 }, "x"));     // no such issue
        Assert.Equal(3, batch.PendingChanges);
        Assert.Null(Read(a).Format);                                        // nothing written yet

        Assert.True(await batch.PreviewAndApplyAsync("Tidy up"));

        var request = Assert.Single(shown);
        Assert.Equal("Tidy up", request.Title);
        Assert.Contains("3 changes to 2 books", request.Message);
        Assert.Contains(request.Items!, line => line == "Kilo Station #1 - Format: (empty) → Trade Paperback");
        var applied = Read(a);
        Assert.Equal(("Trade Paperback", (string?)null), (applied.Format, applied.BookAge));
        Assert.Equal(new[] { "keeper" }, applied.Tags);
        Assert.Equal("Modern", Read(b).BookAge);
    }

    [Fact]
    public async Task Cancelling_the_preview_writes_nothing()
    {
        int a = SeedIssue();
        var batch = Writer(answer: 1, new List<ConfirmDialogRequest>()).BeginBatch();
        batch.SetFormat(new Issue { Id = a }, "Omnibus");

        Assert.False(await batch.PreviewAndApplyAsync("x"));
        Assert.Null(Read(a).Format);
    }

    [Fact]
    public async Task A_batch_that_changes_nothing_shows_no_dialog()
    {
        int a = SeedIssue();
        var shown = new List<ConfirmDialogRequest>();
        var batch = Writer(answer: 0, shown).BeginBatch();
        batch.AddTag(new Issue { Id = a }, "temp");
        batch.RemoveTag(new Issue { Id = a }, "temp");     // back where it started

        Assert.Equal(0, batch.PendingChanges);
        Assert.False(await batch.PreviewAndApplyAsync("x"));
        Assert.Empty(shown);
    }

    [Fact]
    public async Task A_batch_is_used_once()
    {
        int a = SeedIssue();
        var batch = Writer(answer: 0, new List<ConfirmDialogRequest>()).BeginBatch();
        batch.SetFormat(new Issue { Id = a }, "One-shot");
        Assert.True(await batch.PreviewAndApplyAsync("x"));

        Assert.False(batch.SetFormat(new Issue { Id = a }, "Again"));
        Assert.False(await batch.PreviewAndApplyAsync("x"));
    }

    // ================================================================ §C providers

    [Fact]
    public async Task A_provider_call_that_throws_or_hangs_is_unavailable_and_logged()
    {
        var registry = new PluginProviderRegistry(_logs) { Timeout = TimeSpan.FromMilliseconds(300) };

        string? fine = await registry.CallAsync("p", "Search", _ => Task.FromResult("ok"));
        string? failed = await registry.CallAsync<string>("p", "Search", _ => throw new InvalidOperationException("boom"));
        string? hung = await registry.CallAsync<string>("p", "Covers", async ct => { await Task.Delay(Timeout.Infinite, CancellationToken.None); return "never"; });

        Assert.Equal("ok", fine);
        Assert.Null(failed);
        Assert.Null(hung);
        string log = File.ReadAllText(_logs.PathFor("p"));
        Assert.Contains("Search failed: boom", log);
        Assert.Contains("Covers didn't answer", log);
    }

    [Fact]
    public void Registrations_replace_per_plugin_and_are_dropped_with_the_plugin()
    {
        var registry = new PluginProviderRegistry(_logs);
        IPluginProviders a = registry.ForPlugin("a");
        a.RegisterCovers("Old", (_, _) => Task.FromResult<IReadOnlyList<PluginCover>>(Array.Empty<PluginCover>()));
        a.RegisterCovers("New", (_, _) => Task.FromResult<IReadOnlyList<PluginCover>>(Array.Empty<PluginCover>()));
        registry.ForPlugin("b").RegisterArcSource("", browsable: true,
            (_, _) => Task.FromResult<IReadOnlyList<PluginArc>>(Array.Empty<PluginArc>()),
            (_, _) => Task.FromResult<IReadOnlyList<PluginArcEntry>>(Array.Empty<PluginArcEntry>()));

        Assert.Equal("New", Assert.Single(registry.CoverProviders).DisplayName);
        PluginArcSource arc = Assert.Single(registry.ArcSources);
        Assert.Equal("b", arc.DisplayName);                     // a blank name falls back to the key
        Assert.Equal("plugin:b", arc.SourceKey);
        Assert.Same(arc, registry.FindArcSource("plugin:b"));
        Assert.Null(registry.FindArcSource("ComicVine"));

        registry.RetainOnly(new[] { "b" });

        Assert.Empty(registry.CoverProviders);
        Assert.Single(registry.ArcSources);
    }

    [Fact]
    public async Task A_plugin_arc_source_reads_like_any_reading_list_source()
    {
        var registry = new PluginProviderRegistry(_logs);
        registry.ForPlugin("arcs").RegisterArcSource("Arc Shelf", browsable: true,
            (q, _) => Task.FromResult<IReadOnlyList<PluginArc>>(new[] { new PluginArc("a1", $"Found {q}", "Marvel", 1984) }),
            (id, _) => Task.FromResult<IReadOnlyList<PluginArcEntry>>(new[] { new PluginArcEntry("Secret Wars", "1", null, 1984), new PluginArcEntry("Thor", "383") }));
        var source = new PluginReadingListSource(registry, registry.ArcSources.Single());

        var hit = Assert.Single(await source.SearchAsync("wars", CancellationToken.None));
        var issues = await source.GetArcIssuesInOrderAsync("a1", CancellationToken.None);

        Assert.Equal(("plugin:arcs", "Arc Shelf", true), (source.SourceKey, source.DisplayName, source.HasBrowsableCatalog));
        Assert.Equal(("a1", "Found wars", "Marvel"), (hit.Id, hit.Name, hit.Publisher));
        Assert.Equal(new[] { ("Secret Wars", "1", 1984), ("Thor", "383", 0) }, issues.Select(i => (i.Series, i.Number, i.Year)));
    }

    [Fact]
    public async Task A_failing_or_removed_plugin_arc_source_reports_a_source_error()
    {
        var registry = new PluginProviderRegistry(_logs);
        registry.ForPlugin("flaky").RegisterArcSource("Flaky", false,
            (_, _) => throw new InvalidOperationException("down"),
            (_, _) => Task.FromResult<IReadOnlyList<PluginArcEntry>>(Array.Empty<PluginArcEntry>()));
        var flaky = new PluginReadingListSource(registry, registry.ArcSources.Single());

        await Assert.ThrowsAsync<Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceException>(() => flaky.SearchAsync("x", CancellationToken.None));

        var gone = new PluginReadingListSource.Unavailable("plugin:gone");
        var error = await Assert.ThrowsAsync<Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceException>(() => gone.GetArcIssuesInOrderAsync("1", CancellationToken.None));
        Assert.Contains("removed or disabled", error.Message);
    }

    [Fact]
    public void Installing_the_registry_adds_plugin_sources_to_the_reading_list_registry()
    {
        var savedExtra = Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.ExtraSources;
        var savedResolve = Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.ResolveExtra;
        try
        {
            var registry = new PluginProviderRegistry(_logs);
            registry.ForPlugin("arcs").RegisterArcSource("Arc Shelf", false,
                (_, _) => Task.FromResult<IReadOnlyList<PluginArc>>(Array.Empty<PluginArc>()),
                (_, _) => Task.FromResult<IReadOnlyList<PluginArcEntry>>(Array.Empty<PluginArcEntry>()));
            PluginReadingListSource.Install(registry);
            using var context = NewContext();

            Assert.Contains(Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.Available, s => s.Key == "plugin:arcs" && s.DisplayName == "Arc Shelf");
            Assert.Equal("Arc Shelf", Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.GetDisplayName("plugin:arcs"));
            Assert.IsType<PluginReadingListSource>(Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.Get(context, "plugin:arcs"));
            Assert.IsType<PluginReadingListSource.Unavailable>(Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.Get(context, "plugin:gone"));
            Assert.Null(Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.Get(context, "NoSuchSource"));
        }
        finally
        {
            Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.ExtraSources = savedExtra;
            Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.ResolveExtra = savedResolve;
        }
    }

    [Fact]
    public void Plugin_metadata_becomes_pending_proposals_and_nothing_is_applied()
    {
        int seriesId;
        using (var context = NewContext())
        {
            var series = new Series { Name = "Kilo Station", Summary = "Old summary", Genre = "Horror" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
        }

        var metadata = new PluginSeriesMetadata
        {
            Description = "A station at the edge.",
            Status = "finished",
            Genres = new[] { "Horror" },          // same as now: not proposed
            Publisher = "Kilo Press",
        };

        using (var context = NewContext())
        {
            Assert.Equal(3, PluginMetadataProposals.Create(context, seriesId, metadata));
            Assert.Equal(0, PluginMetadataProposals.Create(context, seriesId, metadata));   // the same pending values aren't offered twice
        }

        using (var context = NewContext())
        {
            var series = context.Series.Single(s => s.Id == seriesId);
            Assert.Equal("Old summary", series.Summary);                                 // nothing applied
            var proposals = context.MetadataProposals.Where(p => p.SeriesId == seriesId).ToList();
            Assert.All(proposals, p => Assert.Equal((MetadataProposalStatus.Pending, MetadataProposalSource.Other), (p.Status, p.Source)));
            Assert.Equal(new[] { MetadataProposalField.Publisher, MetadataProposalField.Status, MetadataProposalField.Summary },
                proposals.Select(p => p.Field).OrderBy(f => f.ToString()).ToArray());
            Assert.Equal("Completed", proposals.Single(p => p.Field == MetadataProposalField.Status).ProposedValue);
        }
    }

    // ================================================================ §F http

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<Uri> Sent { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hello") });
        }
    }

    [Fact]
    public async Task Http_reaches_declared_hosts_and_refuses_the_rest()
    {
        var handler = new StubHandler();
        var http = new PluginHttp(new[] { "api.example.com", "*.cdn.example.com" }, "p", new PluginLogger(_logs, "p"), new HttpMessageInvoker(handler));

        Assert.Equal("hello", await http.GetStringAsync("https://api.example.com/v1/x"));
        Assert.Equal("hello", await http.GetStringAsync("https://img.cdn.example.com/c.jpg"));
        var refused = await Assert.ThrowsAsync<PluginNetworkException>(() => http.GetStringAsync("https://tracker.evil.net/"));
        await Assert.ThrowsAsync<PluginNetworkException>(() => http.GetStringAsync("file:///C:/secrets.txt"));

        Assert.Equal(2, handler.Sent.Count);
        Assert.Contains("tracker.evil.net isn't declared", refused.Message);
        Assert.Contains("refused a request", File.ReadAllText(_logs.PathFor("p")));
    }

    private string Package(string manifest)
    {
        string zip = Path.Combine(_root, $"pkg-{Guid.NewGuid():N}.zip");
        using var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("plugin.xml").Open()))
        {
            writer.Write(manifest);
        }

        return zip;
    }

    [Fact]
    public void The_install_confirm_names_the_tier_and_the_declared_hosts()
    {
        string declaring = Package("""<Plugin key="p" name="P"><Network><Host name="api.example.com"/><Host name="*.cdn.example.com"/></Network></Plugin>""");
        string silent = Package("""<Plugin key="q" name="Q"/>""");

        Assert.Equal(new[] { "api.example.com", "*.cdn.example.com" }, PluginScreenViewModel.ReadDeclaredHosts(declaring));
        Assert.Empty(PluginScreenViewModel.ReadDeclaredHosts(silent));
        Assert.Empty(PluginScreenViewModel.ReadDeclaredHosts(Path.Combine(_root, "missing.zip")));

        Assert.Equal("Script plugin.\nDeclared network access: api.example.com, *.cdn.example.com.", PluginScreenViewModel.DescribePackage(null, declaring));
        Assert.EndsWith("No network access declared.", PluginScreenViewModel.DescribePackage(null, silent));
    }

    [Fact]
    public async Task A_plugin_that_declares_no_hosts_can_reach_none()
    {
        var http = new PluginHttp(Array.Empty<string>(), "p", new PluginLogger(_logs, "p"), new HttpMessageInvoker(new StubHandler()));

        var refused = await Assert.ThrowsAsync<PluginNetworkException>(() => http.GetBytesAsync("https://api.example.com/"));
        Assert.Contains("no hosts are declared", refused.Message);
    }
}
