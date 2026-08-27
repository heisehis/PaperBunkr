using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="GraphicsBootstrap"/> (docs/superpowers/specs/2026-08-27-hardware-
/// accelerated-rendering-design.md §5/§8) - pure config resolution, no real GPU or Avalonia
/// bootstrap. Redirects the cache path to a temp file and stubs env-var reads so nothing touches
/// the real process environment or <c>%AppData%</c>.
/// </summary>
public class GraphicsBootstrapTests : IDisposable
{
    private readonly string _cachePath;

    public GraphicsBootstrapTests()
    {
        _cachePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_graphics_test_{Guid.NewGuid():N}.json");
        GraphicsBootstrap.CachePathOverride = _cachePath;
        GraphicsBootstrap.EnvReaderOverride = _ => null;
    }

    public void Dispose()
    {
        GraphicsBootstrap.CachePathOverride = null;
        GraphicsBootstrap.EnvReaderOverride = null;
        try
        {
            if (File.Exists(_cachePath)) File.Delete(_cachePath);
        }
        catch (IOException)
        {
        }
    }

    private void WriteCache(string json) => File.WriteAllText(_cachePath, json);

    [Fact]
    public void Resolve_NoCache_ReturnsDefault()
    {
        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(GraphicsConfig.Default, config);
        Assert.Equal("default (no cache)", source);
    }

    [Theory]
    [InlineData("auto", RenderBackend.Auto)]
    [InlineData("GPU", RenderBackend.Gpu)]
    [InlineData("Software", RenderBackend.Software)]
    public void Resolve_ValidCache_ParsesBackendCaseInsensitively(string backend, RenderBackend expected)
    {
        WriteCache($$"""{ "backend": "{{backend}}", "preferNativeOpenGl": true }""");

        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(expected, config.Backend);
        Assert.True(config.PreferNativeOpenGl);
        Assert.Equal("graphics.json", source);
    }

    [Fact]
    public void Resolve_MalformedJson_ReturnsDefaultWithoutThrowing()
    {
        WriteCache("{ not json at all ");

        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(GraphicsConfig.Default, config);
        Assert.Contains("unreadable", source);
    }

    [Fact]
    public void Resolve_UnknownBackendString_FallsBackToAutoButKeepsPreferNativeOpenGl()
    {
        WriteCache("""{ "backend": "vulkan-ish", "preferNativeOpenGl": true }""");

        var (config, _) = GraphicsBootstrap.Resolve();

        Assert.Equal(RenderBackend.Auto, config.Backend);
        Assert.True(config.PreferNativeOpenGl);
    }

    [Fact]
    public void Resolve_EnvVar_OverridesCacheBackendButNotPreferNativeOpenGl()
    {
        WriteCache("""{ "backend": "gpu", "preferNativeOpenGl": true }""");
        GraphicsBootstrap.EnvReaderOverride = name =>
            name == "PAPERBUNKR_RENDER" ? "software" : null;

        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(RenderBackend.Software, config.Backend);
        Assert.True(config.PreferNativeOpenGl);
        Assert.Equal("graphics.json + env", source);
    }

    [Fact]
    public void Resolve_UnrecognizedEnvValue_IsIgnored()
    {
        GraphicsBootstrap.EnvReaderOverride = _ => "turbo";

        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(GraphicsConfig.Default, config);
        Assert.Equal("default (no cache)", source);
    }

    [Theory]
    [InlineData(RenderBackend.Auto, false, new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software })]
    [InlineData(RenderBackend.Auto, true, new[] { Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software })]
    [InlineData(RenderBackend.Gpu, false, new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl })]
    [InlineData(RenderBackend.Gpu, true, new[] { Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl })]
    [InlineData(RenderBackend.Software, false, new[] { Win32RenderingMode.Software })]
    [InlineData(RenderBackend.Software, true, new[] { Win32RenderingMode.Software })]
    public void ToRenderingModes_MapsEachRowInOrder(RenderBackend backend, bool preferWgl, Win32RenderingMode[] expected)
    {
        var modes = GraphicsBootstrap.ToRenderingModes(new GraphicsConfig(backend, preferWgl));

        Assert.Equal(expected, modes);
    }

    [Fact]
    public void SyncCache_NoFile_WritesWellFormedFileAndReturnsTrue()
    {
        bool wrote = GraphicsBootstrap.SyncCache(RenderBackend.Gpu, preferNativeOpenGl: true);

        Assert.True(wrote);
        using var doc = JsonDocument.Parse(File.ReadAllText(_cachePath));
        Assert.Equal("gpu", doc.RootElement.GetProperty("backend").GetString());
        Assert.True(doc.RootElement.GetProperty("preferNativeOpenGl").GetBoolean());
    }

    [Fact]
    public void SyncCache_FileAlreadyMatches_ReturnsFalseWithoutRewriting()
    {
        GraphicsBootstrap.SyncCache(RenderBackend.Software, preferNativeOpenGl: false);
        var firstWrite = File.GetLastWriteTimeUtc(_cachePath);

        bool wrote = GraphicsBootstrap.SyncCache(RenderBackend.Software, preferNativeOpenGl: false);

        Assert.False(wrote);
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(_cachePath));
    }

    [Fact]
    public void SyncCache_FileDiffers_RewritesAndReturnsTrue()
    {
        GraphicsBootstrap.SyncCache(RenderBackend.Auto, preferNativeOpenGl: false);

        bool wrote = GraphicsBootstrap.SyncCache(RenderBackend.Software, preferNativeOpenGl: false);

        Assert.True(wrote);
        var (config, _) = GraphicsBootstrap.Resolve();
        Assert.Equal(RenderBackend.Software, config.Backend);
    }

    [Fact]
    public void SyncCache_ThenResolve_RoundTrips()
    {
        GraphicsBootstrap.SyncCache(RenderBackend.Gpu, preferNativeOpenGl: true);

        var (config, source) = GraphicsBootstrap.Resolve();

        Assert.Equal(new GraphicsConfig(RenderBackend.Gpu, true), config);
        Assert.Equal("graphics.json", source);
    }

    [Fact]
    public void SyncCache_UnwritablePath_ReturnsFalseWithoutThrowing()
    {
        GraphicsBootstrap.CachePathOverride = "Z:\\does-not-exist\\graphics.json";

        bool wrote = GraphicsBootstrap.SyncCache(RenderBackend.Software, preferNativeOpenGl: false);

        Assert.False(wrote);
    }
}
