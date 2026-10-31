using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Plugin API 4.5 manifest additions (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §D1 and §F): parsing and
/// validating <c>&lt;Fields&gt;</c> and <c>&lt;Network&gt;</c>, normalising field values, matching hosts, and the engine blocking a plugin
/// whose declaration is malformed - through the real manifest reader and a real <see cref="PluginEngine"/>.
/// </summary>
public sealed class PluginFieldsAndNetworkManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-fields-network-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginManifest Manifest(string body)
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, $"m-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, $"""<Plugin key="p" name="P">{body}</Plugin>""");
        return XmlPluginInitializer.ReadManifest(file) ?? throw new InvalidOperationException("manifest didn't parse");
    }

    // ---- fields ----

    [Fact]
    public void Fields_parse_with_their_types_labels_and_storage_names()
    {
        var schema = PluginFieldSchema.Parse("p", Manifest("""
            <Fields>
              <Field key="daysSinceRead" label="Days since read" type="number"/>
              <Field key="mood" type="TEXT"/>
              <Field key="lastSynced" label="Last synced" type="date"/>
              <Field key="flagged" label="Flagged" type="toggle"/>
            </Fields>
            """), out string? error);

        Assert.Null(error);
        Assert.Equal(
            new[] { ("daysSinceRead", "Days since read", SmartListDataType.Number), ("mood", "mood", SmartListDataType.Text),
                    ("lastSynced", "Last synced", SmartListDataType.Date), ("flagged", "Flagged", SmartListDataType.Toggle) },
            schema!.Fields.Select(f => (f.Key, f.Label, f.Type)));
        Assert.Equal("p.daysSinceRead", schema.Find("daysSinceRead")!.StorageName);
    }

    [Theory]
    [InlineData("""<Fields><Field key="a" type="colour"/></Fields>""", "unknown type")]
    [InlineData("""<Fields><Field key="a" type="text"/><Field key="a" type="number"/></Fields>""", "more than once")]
    [InlineData("""<Fields><Field type="text"/></Fields>""", "no key")]
    public void A_malformed_field_is_an_error(string body, string expected)
    {
        Assert.Null(PluginFieldSchema.Parse("p", Manifest(body), out string? error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void No_fields_is_no_schema_and_no_error()
    {
        Assert.Null(PluginFieldSchema.Parse("p", Manifest(""), out string? error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(SmartListDataType.Number, "12", "12")]
    [InlineData(SmartListDataType.Number, " 3.50 ", "3.5")]
    [InlineData(SmartListDataType.Number, "ten", null)]
    [InlineData(SmartListDataType.Date, "2026-10-28", "2026-10-28")]
    [InlineData(SmartListDataType.Date, "2026-10-28T13:45:00Z", "2026-10-28")]
    [InlineData(SmartListDataType.Date, "yesterday", null)]
    [InlineData(SmartListDataType.Toggle, "TRUE", "true")]
    [InlineData(SmartListDataType.Toggle, "yes", null)]
    [InlineData(SmartListDataType.Text, " kept as is ", " kept as is ")]
    public void Values_are_normalised_to_their_stored_form(SmartListDataType type, string value, string? stored) =>
        Assert.Equal(stored, PluginFieldSchema.Normalize(type, value));

    // ---- network ----

    [Fact]
    public void Network_hosts_parse_lower_cased_and_deduplicated()
    {
        var hosts = PluginNetworkHosts.Parse(Manifest("""
            <Network><Host name="API.Example.com"/><Host name="*.cdn.example.com"/><Host name="api.example.com"/></Network>
            """), out string? error);

        Assert.Null(error);
        Assert.Equal(new[] { "api.example.com", "*.cdn.example.com" }, hosts);
    }

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("api.example.com/v1")]
    [InlineData("api.example.com:8080")]
    [InlineData("*.*.example.com")]
    [InlineData("")]
    public void A_host_with_a_scheme_path_port_or_bad_wildcard_is_an_error(string name)
    {
        Assert.Null(PluginNetworkHosts.Parse(Manifest($"""<Network><Host name="{name}"/></Network>"""), out string? error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("api.example.com", true)]
    [InlineData("API.EXAMPLE.COM", true)]
    [InlineData("img.cdn.example.com", true)]
    [InlineData("cdn.example.com", true)]
    [InlineData("example.com", false)]
    [InlineData("evil-api.example.com", false)]
    [InlineData("api.example.com.evil.net", false)]
    public void Hosts_match_exactly_or_under_a_wildcard(string host, bool allowed) =>
        Assert.Equal(allowed, PluginNetworkHosts.Allows(new[] { "api.example.com", "*.cdn.example.com" }, host));

    [Fact]
    public void Describe_names_the_hosts_or_says_none()
    {
        Assert.Equal("No network access declared", PluginNetworkHosts.Describe(Array.Empty<string>()));
        Assert.Equal("Declared network access: a.com, *.b.com", PluginNetworkHosts.Describe(new[] { "a.com", "*.b.com" }));
    }

    // ---- the engine ----

    private void WritePlugin(string key, string body)
    {
        string dir = Path.Combine(_root, "plugins", key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key}">
              <Command hook="Startup" key="{key}.s" name="s" script="s.csx" />
              {body}
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "s.csx"), "return 1;");
    }

    [Fact]
    public void The_engine_records_fields_and_hosts_and_blocks_a_malformed_declaration()
    {
        WritePlugin("good", """<Fields><Field key="score" type="number"/></Fields><Network><Host name="api.example.com"/></Network>""");
        WritePlugin("badfield", """<Fields><Field key="x" type="nope"/></Fields>""");
        WritePlugin("badhost", """<Network><Host name="http://x.com"/></Network>""");
        WritePlugin("plain", "");

        var engine = new PluginEngine();
        engine.Discover(Path.Combine(_root, "plugins"), new FakePluginEnvironment());

        Assert.Equal("good.score", engine.FieldSchemas["good"].Find("score")!.StorageName);
        Assert.Equal(new[] { "api.example.com" }, engine.NetworkHosts["good"]);
        Assert.Empty(engine.NetworkHosts["plain"]);
        Assert.StartsWith("invalid <Fields>", engine.PackageApiInfo["badfield"].BlockedReason);
        Assert.StartsWith("invalid <Network>", engine.PackageApiInfo["badhost"].BlockedReason);
        Assert.DoesNotContain(engine.AllCommands, c => c.PluginKey is "badfield" or "badhost");
    }
}
