using System.IO.Compression;
using Paperbunkr.Data.Library;

namespace Paperbunkr.Data.Tests.Library;

/// <summary><see cref="SwapFileSweeper"/> (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 2).</summary>
public class SwapFileSweeperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_sweeper_{Guid.NewGuid():N}");

    public SwapFileSweeperTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Batman Beyond"));
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

    private string Folder => Path.Combine(_root, "Batman Beyond");

    private string Comic(string name)
    {
        string path = Path.Combine(Folder, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var stream = zip.CreateEntry("001.jpg").Open();
            stream.Write(new byte[] { 1, 2, 3 });
        }

        return path;
    }

    private static string Leftover(string path, bool old = true, bool validZip = true)
    {
        if (validZip)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var stream = zip.CreateEntry("001.jpg").Open();
            stream.Write(new byte[] { 1 });
        }
        else
        {
            File.WriteAllBytes(path, new byte[] { 0 });
        }

        if (old)
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        }

        return path;
    }

    [Fact]
    public void A_crswap_beside_an_intact_comic_is_deleted()
    {
        string comic = Comic("Batman_Beyond_Neo-Gothic_(2023-)_#1.cbz");
        string swap = Leftover(comic + ".crswap");

        var result = SwapFileSweeper.Sweep(new[] { _root });

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(swap));
        Assert.True(File.Exists(comic));
    }

    [Fact]
    public void Each_known_pattern_is_cleaned()
    {
        string comic = Comic("issue 2.cbz");
        string pb = Leftover(comic + ".pbwrite-0123456789abcdef.tmp");
        string rf = Leftover(Path.Combine(Folder, "issue 2~RF1a2b3c.TMP"));

        var result = SwapFileSweeper.Sweep(new[] { _root });

        Assert.Equal(2, result.Deleted);
        Assert.False(File.Exists(pb));
        Assert.False(File.Exists(rf));
    }

    [Fact]
    public void A_leftover_whose_comic_is_missing_is_kept_as_a_restore_candidate()
    {
        string missing = Path.Combine(Folder, "gone.cbz");
        string swap = Leftover(missing + ".crswap");

        var result = SwapFileSweeper.Sweep(new[] { _root });

        Assert.Equal(0, result.Deleted);
        var candidate = Assert.Single(result.RestoreCandidates);
        Assert.Equal(missing, candidate.OriginalPath);
        Assert.False(candidate.OriginalExists);
        Assert.True(candidate.LeftoverIsValidArchive);
        Assert.True(File.Exists(swap));
    }

    [Fact]
    public void A_leftover_beside_a_damaged_comic_is_kept()
    {
        string comic = Path.Combine(Folder, "broken.cbz");
        File.WriteAllBytes(comic, new byte[] { (byte)'P', (byte)'K', 0, 0, 9, 9 });
        string swap = Leftover(comic + ".crswap");

        var result = SwapFileSweeper.Sweep(new[] { _root });

        Assert.Single(result.RestoreCandidates);
        Assert.True(File.Exists(swap));
    }

    [Fact]
    public void A_fresh_leftover_is_skipped_because_a_write_may_still_own_it()
    {
        string comic = Comic("busy.cbz");
        string swap = Leftover(comic + ".crswap", old: false);

        var result = SwapFileSweeper.Sweep(new[] { _root });

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(swap));
    }

    [Fact]
    public void A_locked_leftover_is_skipped_not_thrown()
    {
        string comic = Comic("locked.cbz");
        string swap = Leftover(comic + ".crswap");

        using (new FileStream(swap, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = SwapFileSweeper.Sweep(new[] { _root });
            Assert.Equal(0, result.Deleted);
            Assert.Equal(1, result.Skipped);
        }

        Assert.True(File.Exists(swap));
    }

    [Fact]
    public void Restore_moves_the_copy_back_only_when_the_comic_is_missing()
    {
        string missing = Path.Combine(Folder, "gone.cbz");
        Leftover(missing + ".crswap");
        var candidate = Assert.Single(SwapFileSweeper.Sweep(new[] { _root }).RestoreCandidates);

        Assert.Equal(missing, SwapFileSweeper.Restore(candidate));
        Assert.True(File.Exists(missing));

        string other = Path.Combine(Folder, "again.cbz");
        Leftover(other + ".crswap");
        var second = Assert.Single(SwapFileSweeper.Sweep(new[] { _root }).RestoreCandidates);
        Comic("again.cbz");
        Assert.Throws<IOException>(() => SwapFileSweeper.Restore(second));
    }

    [Fact]
    public void SweepBeside_only_looks_next_to_the_given_files()
    {
        string comic = Comic("one.cbz");
        string swap = Leftover(comic + ".crswap");
        string elsewhere = Path.Combine(_root, "Other");
        Directory.CreateDirectory(elsewhere);
        string otherComic = Path.Combine(elsewhere, "two.cbz");
        File.Copy(comic, otherComic);
        string otherSwap = Leftover(otherComic + ".crswap");

        var result = SwapFileSweeper.SweepBeside(new[] { comic });

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(swap));
        Assert.True(File.Exists(otherSwap));
    }

    [Theory]
    [InlineData("x.cbz.crswap", true)]
    [InlineData("x.cbz.pbwrite-0a1b.tmp", true)]
    [InlineData("x~RF12ab.TMP", true)]
    [InlineData("x.cbz", false)]
    [InlineData("notes.tmp", false)]
    public void Recognises_leftover_names(string name, bool expected) => Assert.Equal(expected, SwapFileSweeper.IsLeftoverName(name));
}
