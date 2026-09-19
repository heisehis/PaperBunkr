using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="FileAssociationService"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-advanced-tab-design.md §2) against an in-memory fake
/// <see cref="IShellFileAssociation"/> - never touches the real Windows registry.
/// </summary>
public class FileAssociationServiceTests
{
    [Fact]
    public void GetAvailableFormats_ListsRegisteredFormats_NoneAssociatedInitially()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        var formats = service.GetAvailableFormats();

        Assert.NotEmpty(formats);
        Assert.All(formats, f => Assert.False(f.IsAssociated));
    }

    [Fact]
    public void SetAssociated_True_RegistersEveryExtensionInThatFormat_AndRefreshesShell()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);
        var format = service.GetAvailableFormats().First();

        service.SetAssociated(format.Name, true);

        var refreshed = service.GetAvailableFormats().First(f => f.Name == format.Name);
        Assert.True(refreshed.IsAssociated);
        Assert.True(fake.RefreshCalled);
    }

    [Fact]
    public void SetAssociated_FalseAfterTrue_UnregistersEveryExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);
        var format = service.GetAvailableFormats().First();

        service.SetAssociated(format.Name, true);
        service.SetAssociated(format.Name, false);

        var refreshed = service.GetAvailableFormats().First(f => f.Name == format.Name);
        Assert.False(refreshed.IsAssociated);
    }

    [Fact]
    public void SetAssociated_UnknownFormatName_DoesNothing_NoException()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetAssociated("Not A Real Format", true);

        Assert.False(fake.RefreshCalled);
    }

    // --- Comic-format scoping (docs/superpowers/specs/2026-09-09-installer-redesign-design.md
    //     decision 5, + the 2026-09-09 .cbt-parity correction). Drives the installer's per-format
    //     [Tasks] and Program.cs's --register-file-associations CLI path. ---

    [Fact]
    public void ComicAssociationExtensions_IsExactlyTheSevenComicSpecificExtensions()
    {
        Assert.Equal(
            new[] { ".pdf", ".cbz", ".cbr", ".cb7", ".cbt", ".cbw", ".djvu" }.OrderBy(x => x),
            FileAssociationService.ComicAssociationExtensions.OrderBy(x => x));
    }

    [Fact]
    public void SetComicAssociationsFor_SingleComicExtension_RegistersOnlyThatExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cbz" }, true);

        Assert.Contains(".cbz", fake.RegisteredExtensions);
        Assert.DoesNotContain(".zip", fake.RegisteredExtensions);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
        Assert.DoesNotContain(".7z", fake.RegisteredExtensions);
        Assert.DoesNotContain(".cbr", fake.RegisteredExtensions);
        Assert.DoesNotContain(".pdf", fake.RegisteredExtensions);
        Assert.True(fake.RefreshCalled);
    }

    [Fact]
    public void SetComicAssociationsFor_Cbr_CoversBothTheRarAndRar5ProviderRegistrations_NotBareRar()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cbr" }, true);

        // Both "eComic (RAR)" and "eComic (RAR5)" claim .cbr - one task, both registered.
        Assert.Contains(".cbr", fake.RegisteredExtensions);
        var cbrTypeIds = fake.RegistrationsFor(".cbr").ToList();
        Assert.Equal(2, cbrTypeIds.Count);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetComicAssociationsFor_FullAllowList_RegistersTheArchiveComicFormats_NeverGenericArchives()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(FileAssociationService.ComicAssociationExtensions, true);

        // The archive-backed comic providers (Cbz/Cbr/Rar5/Cb7/Cbt/WebComic) are always registered.
        // .pdf and .djvu ride on native deps (pdfium / djvm.exe) whose providers are
        // IValidateProvider and may be absent in a headless test bin dir - not asserted here.
        foreach (var ext in new[] { ".cbz", ".cbr", ".cb7", ".cbt", ".cbw" })
        {
            Assert.Contains(ext, fake.RegisteredExtensions);
        }

        // The whole point of the scoping: bare archive extensions are never touched.
        Assert.DoesNotContain(".zip", fake.RegisteredExtensions);
        Assert.DoesNotContain(".rar", fake.RegisteredExtensions);
        Assert.DoesNotContain(".7z", fake.RegisteredExtensions);
        Assert.DoesNotContain(".tar", fake.RegisteredExtensions);
        Assert.All(fake.RegisteredExtensions,
            ext => Assert.Contains(ext, FileAssociationService.ComicAssociationExtensions));
    }

    [Fact]
    public void SetComicAssociationsFor_GenericArchiveExtension_RegistersNothing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".zip", ".rar", ".7z" }, true);

        Assert.Empty(fake.RegisteredExtensions);
        Assert.False(fake.RefreshCalled);
    }

    [Fact]
    public void SetComicAssociationsFor_ToleratesMissingLeadingDotAndCasing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { "CBZ" }, true);

        Assert.Contains(".cbz", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetComicAssociationsFor_FalseAfterTrue_UnregistersThatExtension()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".cb7" }, true);
        service.SetComicAssociationsFor(new[] { ".cb7" }, false);

        Assert.DoesNotContain(".cb7", fake.RegisteredExtensions);
    }

    // --- Book-format scoping (docs/superpowers/specs/2026-09-16-book-file-associations-design.md) ---

    [Fact]
    public void BookAssociationExtensions_CoversEpubFb2ZipMobiAzwAzw3_NotPdf()
    {
        Assert.Equal(
            new[] { ".epub", ".fb2", ".zip", ".mobi", ".azw", ".azw3" }.OrderBy(x => x),
            FileAssociationService.BookAssociationExtensions.OrderBy(x => x));
        Assert.DoesNotContain(".pdf", FileAssociationService.BookAssociationExtensions);
    }

    [Fact]
    public void GetAvailableFormats_IncludesBookFormats()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        var formats = service.GetAvailableFormats();

        Assert.Contains(formats, f => f.Name == "EPUB" && f.ExtensionList == ".epub");
        Assert.Contains(formats, f => f.Name == "FB2" && f.ExtensionList == ".fb2, .zip");
        Assert.Contains(formats, f => f.Name == "Kindle / MOBI" && f.ExtensionList == ".mobi, .azw, .azw3");
    }

    [Fact]
    public void SetAssociated_BookFormatName_RegistersItsExtensions()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetAssociated("EPUB", true);

        Assert.Contains(".epub", fake.RegisteredExtensions);
        var refreshed = service.GetAvailableFormats().First(f => f.Name == "EPUB");
        Assert.True(refreshed.IsAssociated);
    }

    [Fact]
    public void SetBookAssociationsFor_Epub_RegistersOnlyEpub()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetBookAssociationsFor(new[] { ".epub" }, true);

        Assert.Contains(".epub", fake.RegisteredExtensions);
        Assert.DoesNotContain(".fb2", fake.RegisteredExtensions);
        Assert.DoesNotContain(".mobi", fake.RegisteredExtensions);
        Assert.True(fake.RefreshCalled);
    }

    [Fact]
    public void SetBookAssociationsFor_Fb2_RegistersBothFb2AndZip()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetBookAssociationsFor(new[] { ".fb2" }, true);

        // A single "FB2" task covers both extensions - toggling it registers .zip too (a deliberate,
        // documented tradeoff: Windows can't key an association off the compound ".fb2.zip").
        Assert.Contains(".fb2", fake.RegisteredExtensions);
        Assert.Contains(".zip", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetBookAssociationsFor_Mobi_RegistersMobiAzwAndAzw3Together()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetBookAssociationsFor(new[] { ".mobi" }, true);

        Assert.Contains(".mobi", fake.RegisteredExtensions);
        Assert.Contains(".azw", fake.RegisteredExtensions);
        Assert.Contains(".azw3", fake.RegisteredExtensions);
    }

    [Fact]
    public void SetBookAssociationsFor_ComicExtension_RegistersNothing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetBookAssociationsFor(new[] { ".cbz", ".pdf" }, true);

        Assert.Empty(fake.RegisteredExtensions);
        Assert.False(fake.RefreshCalled);
    }

    [Fact]
    public void SetComicAssociationsFor_BookExtension_RegistersNothing()
    {
        var fake = new FakeShellFileAssociation();
        var service = new FileAssociationService(fake);

        service.SetComicAssociationsFor(new[] { ".epub", ".mobi" }, true);

        Assert.Empty(fake.RegisteredExtensions);
        Assert.False(fake.RefreshCalled);
    }

    private sealed class FakeShellFileAssociation : IShellFileAssociation
    {
        private readonly HashSet<(string TypeId, string Extension)> _registered = new();

        public bool RefreshCalled { get; private set; }

        public IEnumerable<string> RegisteredExtensions => _registered.Select(r => r.Extension).Distinct();

        public IEnumerable<string> RegistrationsFor(string extension) =>
            _registered.Where(r => r.Extension == extension).Select(r => r.TypeId);

        public bool IsRegistered(string typeId, string extension) => _registered.Contains((typeId, extension));

        public void Register(string typeId, string extension, string displayName, string appPath) => _registered.Add((typeId, extension));

        public void Unregister(string typeId, string extension) => _registered.Remove((typeId, extension));

        public void RefreshShell() => RefreshCalled = true;
    }
}
