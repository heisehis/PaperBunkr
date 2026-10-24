using System.IO.Compression;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// Safe, staged <c>.crpck</c> installation (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 1b): a package that
/// fails for any reason must leave an already-installed theme of the same name exactly as it was, and nothing unsafe may land on disk.
/// </summary>
public class ThemePackageInstallerTests : IDisposable
{
    private const string GoodLegacy = """{"name":"Good","mode":"Dark","colors":{"bg":"#111111","text":"#EEEEEE","accent":"#4488FF"}}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_installer_{Guid.NewGuid():N}");
    private readonly string _installed;
    private readonly string _extracted;
    private readonly string _packages;

    public ThemePackageInstallerTests()
    {
        _installed = Path.Combine(_root, "skins");
        _extracted = Path.Combine(_root, "skins-extracted");
        _packages = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_packages);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Builds a package named <paramref name="key"/> from (entry path, bytes) pairs. Entry names are written verbatim, so a test can craft an unsafe one.</summary>
    private string Package(string key, params (string Path, byte[] Content)[] entries)
    {
        string folder = Path.Combine(_packages, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, key + ".crpck");
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            using var entryStream = archive.CreateEntry(entryPath).Open();
            entryStream.Write(content);
        }

        return path;
    }

    private static (string, byte[]) Text(string path, string text) => (path, System.Text.Encoding.UTF8.GetBytes(text));

    private ThemeInstallResult Install(string package, ThemePackageLimits? limits = null) =>
        ThemePackageInstaller.Install(package, _installed, _extracted, limits);

    private string ThemeJsonOf(string key) => File.ReadAllText(Path.Combine(_extracted, key, "theme.json"));

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ALegacyPackage_Installs_AndKeepsTheOriginalArchive()
    {
        var result = Install(Package("good", Text("theme.json", GoodLegacy)));

        Assert.True(result.Success, result.Error);
        Assert.Equal("good", result.Key);
        Assert.Contains("\"Good\"", ThemeJsonOf("good"));
        Assert.True(File.Exists(Path.Combine(_installed, "good.crpck")));
    }

    [Fact]
    public void AFailedReinstall_LeavesTheWorkingThemeUntouched()
    {
        Assert.True(Install(Package("mine", Text("theme.json", GoodLegacy))).Success);

        var result = Install(Package("mine", Text("theme.json", "{ not json")));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("\"Good\"", ThemeJsonOf("mine"));                       // the previous version is still there and still valid
    }

    [Fact]
    public void ASuccessfulReinstall_ReplacesTheThemeCompletely()
    {
        Assert.True(Install(Package("mine", Text("theme.json", GoodLegacy), ("assets/old.png", Png(4, 4)))).Success);

        Assert.True(Install(Package("mine", Text("theme.json", GoodLegacy.Replace("Good", "Better")))).Success);

        Assert.Contains("\"Better\"", ThemeJsonOf("mine"));
        Assert.False(File.Exists(Path.Combine(_extracted, "mine", "assets", "old.png")));     // no leftovers from the old version
    }

    [Fact]
    public void NoStagingOrBackupFolders_AreLeftBehind_OnSuccessOrFailure()
    {
        Install(Package("a", Text("theme.json", GoodLegacy)));
        Install(Package("a", Text("theme.json", GoodLegacy)));
        Install(Package("b", Text("theme.json", "{ nope")));

        Assert.Equal(new[] { "a" }, Directory.GetDirectories(_extracted).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("assets/../../evil.json")]
    [InlineData("/rooted.json")]
    public void APathThatEscapesThePackage_IsRefused_AndNothingIsWritten(string entryPath)
    {
        var result = Install(Package("sneaky", Text("theme.json", GoodLegacy), Text(entryPath, "{}")));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ThemeDiagnosticCodes.UnsafePackage);
        Assert.False(Directory.Exists(Path.Combine(_extracted, "sneaky")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.json")));
        Assert.False(File.Exists(Path.Combine(_extracted, "evil.json")));
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("assets/helper.dll")]
    [InlineData("run.ps1")]
    [InlineData("plugin.pbpck")]
    public void APackageContainingAProgram_IsRefused(string entryPath)
    {
        var result = Install(Package("trojan", Text("theme.json", GoodLegacy), (entryPath, new byte[] { 1, 2, 3 })));

        Assert.False(result.Success);
        Assert.Contains("data only", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_extracted, "trojan")));
    }

    [Fact]
    public void TooManyFiles_IsRefused()
    {
        var entries = Enumerable.Range(0, 6).Select(i => Text($"assets/n{i}.txt", "x")).Prepend(Text("theme.json", GoodLegacy)).ToArray();

        var result = Install(Package("many", entries), new ThemePackageLimits { MaxEntries = 5 });

        Assert.False(result.Success);
        Assert.Contains("limit", result.Error);
    }

    [Fact]
    public void APackageThatUnpacksTooLarge_IsRefused_EvenThoughItCompressesSmall()
    {
        // 4 MB of zeros compresses to a few KB: the limit has to count real unpacked bytes, not the archive's size.
        var result = Install(
            Package("bomb", Text("theme.json", GoodLegacy), ("assets/zeros.txt", new byte[4 * 1024 * 1024])),
            new ThemePackageLimits { MaxTotalBytes = 1024 * 1024, MaxEntryBytes = 16 * 1024 * 1024 });

        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_extracted, "bomb")));
    }

    [Fact]
    public void AMissingThemeJson_IsRefused()
    {
        var result = Install(Package("empty", Text("readme.txt", "hello")));

        Assert.False(result.Success);
        Assert.Contains("theme.json", result.Error);
    }

    [Fact]
    public void AFileThatIsNotAZip_IsRefused_WithoutThrowing()
    {
        string path = Path.Combine(_packages, "notzip.crpck");
        File.WriteAllText(path, "this is not a zip archive");

        var result = Install(path);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ABrokenImage_IsLeftOut_WithAWarning_AndTheThemeStillInstalls()
    {
        var result = Install(Package("art", Text("theme.json", GoodLegacy), ("assets/ok.png", Png(8, 8)), ("assets/broken.png", new byte[] { 1, 2, 3, 4 })));

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(Path.Combine(_extracted, "art", "assets", "ok.png")));
        Assert.False(File.Exists(Path.Combine(_extracted, "art", "assets", "broken.png")));
        Assert.Contains(result.Warnings, w => w.Code == ThemeDiagnosticCodes.InvalidAsset && w.PropertyPath == "assets/broken.png");
    }

    [Fact]
    public void AnOversizedImage_IsLeftOut()
    {
        var result = Install(Package("big", Text("theme.json", GoodLegacy), ("assets/wide.png", Png(64, 8))), new ThemePackageLimits { MaxImageDimension = 32 });

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Path.Combine(_extracted, "big", "assets", "wide.png")));
    }

    [Fact]
    public void AFakeFont_IsLeftOut()
    {
        var result = Install(Package("type", Text("theme.json", GoodLegacy), ("assets/fonts/fake.ttf", new byte[] { 9, 9, 9, 9, 9 })));

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Path.Combine(_extracted, "type", "assets", "fonts", "fake.ttf")));
    }

    [Fact]
    public void AnUnknownFileType_IsSkipped_NotInstalled()
    {
        var result = Install(Package("misc", Text("theme.json", GoodLegacy), ("notes.xyz", new byte[] { 1 })));

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(Path.Combine(_extracted, "misc", "notes.xyz")));
        Assert.Contains(result.Warnings, w => w.PropertyPath == "notes.xyz");
    }

    [Fact]
    public void AV2Package_WithALayoutFile_Installs_AndAReferenceCycleIsRefused()
    {
        const string v2 = """{ "schemaVersion": 2, "id": "org.test.v2", "name": "V2", "base": { "palette": { "canvas": "#101010", "ink": "#EEEEEE", "accent": "#55AAFF" }, "layouts": { "Home": "layouts/home.json" } }, "variants": { "dark": { "mode": "Dark" }, "light": { "mode": "Light" } } }""";
        var ok = Install(Package("v2", Text("theme.json", v2), Text("layouts/home.json", """{ "root": { "id": "root", "type": "Stack" } }""")));
        Assert.True(ok.Success, ok.Error);

        const string cyclic = """{ "schemaVersion": 2, "name": "Loop", "base": { "semantic": { "surface.canvas": { "ref": "text.primary" }, "text.primary": { "ref": "surface.canvas" } } } }""";
        var refused = Install(Package("loop", Text("theme.json", cyclic)));
        Assert.False(refused.Success);
        Assert.Contains(refused.Diagnostics, d => d.Code == ThemeDiagnosticCodes.ReferenceCycle);
    }

    [Fact]
    public void AMissingReferencedIcon_Installs_WithAFallbackWarning()
    {
        const string v2 = """{ "schemaVersion": 2, "name": "Icons", "base": { "palette": { "canvas": "#101010" }, "icons": { "navigation.home": "assets/icons/home.png" } } }""";

        var result = Install(Package("icons", Text("theme.json", v2)));

        Assert.True(result.Success, result.Error);
        Assert.Contains(result.Warnings, w => w.Code == ThemeDiagnosticCodes.InvalidAsset && w.FallbackUsed);
    }

    [Fact]
    public void ANewerSchema_IsRefused()
    {
        var result = Install(Package("future", Text("theme.json", """{ "schemaVersion": 9, "name": "Future" }""")));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ThemeDiagnosticCodes.UnsupportedSchema);
    }
}
