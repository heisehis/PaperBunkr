using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary><see cref="ComicVineImagePacer"/>: ComicVine's own cover images are fetched one at a time, spaced like its API (reported 2026-10-21).</summary>
[Collection("ScrapeOrchestratorStatics")]
public sealed class ComicVineImagePacerTests : IDisposable
{
    private readonly List<TimeSpan> _waits = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _savedDelay = ComicVineImagePacer.Delay;

    public ComicVineImagePacerTests() => ComicVineImagePacer.Delay = (t, _) => { _waits.Add(t); return Task.CompletedTask; };

    public void Dispose() => ComicVineImagePacer.Delay = _savedDelay;

    [Theory]
    [InlineData("https://comicvine.gamespot.com/a/uploads/scale_medium/1/cover.jpg", true)]
    [InlineData("https://static.metron.cloud/media/issue/2026/cover.jpg", false)]
    [InlineData("not a url", false)]
    public void Only_ComicVine_own_images_are_paced(string url, bool paced) => Assert.Equal(paced, ComicVineImagePacer.IsComicVineImage(url));

    [Fact]
    public async Task Back_to_back_downloads_are_spaced_apart()
    {
        await ComicVineImagePacer.RunAsync(() => Task.FromResult(1), CancellationToken.None);
        await ComicVineImagePacer.RunAsync(() => Task.FromResult(2), CancellationToken.None);

        Assert.Contains(_waits, w => w > TimeSpan.FromMilliseconds(900) && w <= ComicVineImagePacer.Spacing);
    }
}
