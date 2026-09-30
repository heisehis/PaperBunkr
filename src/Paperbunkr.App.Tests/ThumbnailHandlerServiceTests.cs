using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ThumbnailHandlerService"/> (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md §2)
/// against <see cref="FakeThumbnailRegistry"/> - never touches the real registry.
/// </summary>
public class ThumbnailHandlerServiceTests
{
    private const string CbzShellEx = @".cbz\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey;
    private const string OtherClsid = "{11111111-2222-3333-4444-555555555555}";

    private static (ThumbnailHandlerService Service, FakeThumbnailRegistry Registry) Create(bool withHandler = true, bool withDdjvu = false)
    {
        var registry = new FakeThumbnailRegistry();
        return (new ThumbnailHandlerService(registry, FakeThumbnailRegistry.CreateAppDirectory(withHandler, withDdjvu)), registry);
    }

    [Fact]
    public void Enable_RegistersClassAndExtension_AndIsEnabled()
    {
        var (service, registry) = Create();

        service.SetEnabled(new[] { ".cbz" }, true);

        Assert.True(service.IsEnabled(".cbz"));
        Assert.Equal(ThumbnailHandlerService.HandlerClsid, registry.GetClassesValue(CbzShellEx));
        Assert.Equal(service.HandlerDllPath, registry.GetClassesValue(@"CLSID\" + ThumbnailHandlerService.HandlerClsid + @"\InprocServer32"));
        Assert.Equal("Apartment", registry.GetClassesValue(@"CLSID\" + ThumbnailHandlerService.HandlerClsid + @"\InprocServer32", "ThreadingModel"));
        Assert.Equal(1, registry.RefreshCount);
    }

    [Fact]
    public void Disable_RestoresThePreviousPerUserHandler()
    {
        var (service, registry) = Create();
        registry.SetClassesValue(CbzShellEx, null, OtherClsid);

        service.SetEnabled(new[] { ".cbz" }, true);
        service.SetEnabled(new[] { ".cbz" }, false);

        Assert.Equal(OtherClsid, registry.GetClassesValue(CbzShellEx));
        Assert.False(service.IsEnabled(".cbz"));
        Assert.Empty(registry.SavedExtensions());
    }

    [Fact]
    public void Disable_WhenNothingWasThereBefore_RemovesOurKeysAndTheClass()
    {
        var (service, registry) = Create();

        service.SetEnabled(new[] { ".cbz" }, true);
        service.SetEnabled(new[] { ".cbz" }, false);

        Assert.False(registry.ClassesKeyExists(CbzShellEx));
        Assert.False(registry.ClassesKeyExists(@"CLSID\" + ThumbnailHandlerService.HandlerClsid));
    }

    [Fact]
    public void Enable_UnderPaperbunkrsOwnProgId_AlsoRegistersThere()
    {
        var (service, registry) = Create();
        registry.SetClassesValue(".cbz", null, "Paperbunkr.eComicZIP");
        registry.SetClassesValue("Paperbunkr.eComicZIP", null, "eComic (ZIP)");

        service.SetEnabled(new[] { ".cbz" }, true);

        Assert.Equal(ThumbnailHandlerService.HandlerClsid, registry.GetClassesValue(@"Paperbunkr.eComicZIP\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey));
        Assert.True(service.IsEnabled(".cbz"));
    }

    [Fact]
    public void Enable_NeverWritesIntoAnotherProgramsProgId()
    {
        var (service, registry) = Create();
        registry.SetClassesValue(".cbz", null, "CDisplayEx.cbz");

        service.SetEnabled(new[] { ".cbz" }, true);

        Assert.False(registry.ClassesKeyExists(@"CDisplayEx.cbz\ShellEx"));
    }

    [Fact]
    public void ReapplyAfterAssociationChange_AddsTheNewProgIdLocation()
    {
        var (service, registry) = Create();
        service.SetEnabled(new[] { ".cbz" }, true);
        registry.SetClassesValue(".cbz", null, "Paperbunkr.eComicZIP");
        registry.SetClassesValue("Paperbunkr.eComicZIP", null, "eComic (ZIP)");
        Assert.False(service.IsEnabled(".cbz")); // Windows now checks the ProgID first

        service.ReapplyAfterAssociationChange();

        Assert.True(service.IsEnabled(".cbz"));
    }

    [Fact]
    public void GetOtherOwner_NamesTheMachineWideHandler()
    {
        var (service, registry) = Create();
        registry.Machine[@".pdf\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey] = OtherClsid;
        registry.Descriptions[OtherClsid] = "SumatraPDF";

        Assert.Equal("SumatraPDF", service.GetOtherOwner(".pdf"));
        Assert.Null(service.GetOtherOwner(".cbz"));
    }

    [Fact]
    public void UnownedExtensions_SkipsTypesAnotherProgramOwns()
    {
        var (service, registry) = Create();
        registry.Machine[@".pdf\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey] = OtherClsid;

        var unowned = service.UnownedExtensions();

        Assert.DoesNotContain(".pdf", unowned);
        Assert.Contains(".cbz", unowned);
    }

    [Fact]
    public void DisableAll_RemovesEveryRegistration()
    {
        var (service, registry) = Create();
        service.SetEnabled(new[] { ".cbz", ".epub", ".mobi" }, true);

        service.DisableAll();

        Assert.Empty(registry.SavedExtensions());
        Assert.False(service.IsEnabled(".epub"));
        Assert.False(registry.ClassesKeyExists(@"CLSID\" + ThumbnailHandlerService.HandlerClsid));
    }

    [Fact]
    public void Djvu_IsOnlyOfferedWhenDdjvuIsInstalled()
    {
        Assert.DoesNotContain(".djvu", Create(withDdjvu: false).Service.SupportedExtensions);
        Assert.Contains(".djvu", Create(withDdjvu: true).Service.SupportedExtensions);
    }

    [Fact]
    public void Enable_WithoutTheHandlerDll_ThrowsInsteadOfRegisteringADeadHandler()
    {
        var (service, registry) = Create(withHandler: false);

        Assert.Throws<FileNotFoundException>(() => service.SetEnabled(new[] { ".cbz" }, true));
        Assert.Empty(registry.SavedExtensions());
    }

    [Fact]
    public void UnsupportedExtensions_AreIgnored()
    {
        var (service, registry) = Create();

        service.SetEnabled(new[] { ".zip", ".fb2" }, true);

        Assert.Empty(registry.SavedExtensions());
    }
}
