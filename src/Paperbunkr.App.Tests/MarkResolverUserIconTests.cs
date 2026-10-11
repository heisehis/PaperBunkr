using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The user publisher-icon folder (docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md): files in
/// <c>%APPDATA%\Paperbunkr\publisher-icons</c> are indexed with the CE filename rules and consulted before every bundled mark.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MarkResolverUserIconTests : IDisposable
{
    private readonly string? _originalRoot;
    private readonly string _root;

    public MarkResolverUserIconTests()
    {
        _originalRoot = AppDataPaths.RootOverride;
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_usericons_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        AppDataPaths.RootOverride = _root;
    }

    public void Dispose()
    {
        AppDataPaths.RootOverride = _originalRoot;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string IconsFolder => MarkResolver.UserIconsFolder;

    private static string Drop(string fileName, string? subfolder = null)
    {
        string dir = subfolder is null ? IconsFolder : Path.Combine(IconsFolder, subfolder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // a stand-in: resolving never decodes the image
        return path;
    }

    [Fact]
    public void NoFolder_MeansNoUserIcons_AndTheBundledMarksStillResolve()
    {
        var resolver = new MarkResolver();

        Assert.Equal(0, resolver.UserIconCount);
        Assert.Equal(MarkKind.SvgAsset, resolver.ResolvePublisher("Marvel").Kind);
    }

    [Fact]
    public void AFileNamedAfterAPublisher_IsUsed_AsARasterAtItsOwnPath()
    {
        string path = Drop("Penguin Group.png");
        var resolver = new MarkResolver();

        var spec = resolver.ResolvePublisher("Penguin Group");

        Assert.Equal(MarkKind.Raster, spec.Kind);
        Assert.Equal(path, spec.AssetPath);
        Assert.Equal(1, resolver.UserIconCount);
    }

    [Fact]
    public void AUserIcon_BeatsTheBundledSvg_ForTheSamePublisher()
    {
        string path = Drop("Marvel.png");
        var resolver = new MarkResolver();

        var spec = resolver.ResolvePublisher("Marvel Comics"); // alias of the curated Marvel SVG

        Assert.Equal(MarkKind.Raster, spec.Kind);
        Assert.Equal(path, spec.AssetPath);
    }

    [Fact]
    public void CeFilenameRules_ApplyToUserFiles_AliasesAndEras()
    {
        string alias = Drop("Acme#Acme Publishing.png");
        string old = Drop("Old House(1950-1970).png");
        string recent = Drop("Old House(1990-2005).png");
        var resolver = new MarkResolver();

        Assert.Equal(alias, resolver.ResolvePublisher("Acme").AssetPath);
        Assert.Equal(alias, resolver.ResolvePublisher("Acme Publishing").AssetPath);
        Assert.Equal(old, resolver.ResolvePublisher("Old House", year: 1960).AssetPath);
        Assert.Equal(recent, resolver.ResolvePublisher("Old House", year: 2000).AssetPath);
    }

    [Fact]
    public void SubfoldersAreRead_AndFilesThatAreNotImagesAreIgnored()
    {
        string nested = Drop("Nested Press.jpg", subfolder: "extras");
        Drop("notes.txt");
        Drop("Doc House.svg"); // SVG is not a supported user format
        var resolver = new MarkResolver();

        Assert.Equal(nested, resolver.ResolvePublisher("Nested Press").AssetPath);
        Assert.NotEqual(MarkKind.Raster, resolver.ResolvePublisher("Doc House").Kind);
        Assert.NotEqual(MarkKind.Raster, resolver.ResolvePublisher("notes").Kind);
    }

    [Fact]
    public void ReloadUserIcons_PicksUpFilesAddedAfterLaunch()
    {
        var resolver = new MarkResolver();
        Assert.NotEqual(MarkKind.Raster, resolver.ResolvePublisher("Late Arrival").Kind);

        string path = Drop("Late Arrival.png");
        resolver.ReloadUserIcons();

        Assert.Equal(path, resolver.ResolvePublisher("Late Arrival").AssetPath);
        Assert.Equal(1, resolver.UserIconCount);
    }

    [Fact]
    public void MapIni_InTheUserFolder_AddsExtraKeys()
    {
        string path = Drop("logo-17.png");
        File.WriteAllText(Path.Combine(IconsFolder, "map.ini"), "logo-17.png=Weird/Name Co\n");
        var resolver = new MarkResolver();

        Assert.Equal(path, resolver.ResolvePublisher("Weird/Name Co").AssetPath);
    }

    [Fact]
    public void EnsureUserIconsFolder_CreatesIt()
    {
        Assert.False(Directory.Exists(IconsFolder));

        string created = MarkResolver.EnsureUserIconsFolder();

        Assert.Equal(IconsFolder, created);
        Assert.True(Directory.Exists(created));
    }
}
