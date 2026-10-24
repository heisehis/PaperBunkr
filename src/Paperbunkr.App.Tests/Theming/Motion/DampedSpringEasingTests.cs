using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Tests.Theming.Motion;

/// <summary>Spec §3.2: one ~3 % overshoot near 62 % of the duration, no second bounce, invisible end snap.</summary>
public sealed class DampedSpringEasingTests
{
    private readonly DampedSpringEasing _spring = new();

    [Fact]
    public void StartsAtZero_EndsExactlyAtOne()
    {
        Assert.Equal(0, _spring.Ease(0));
        Assert.Equal(1, _spring.Ease(1));
    }

    [Fact]
    public void OvershootsOnceByAboutThreePercent_ThenSettles()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => (T: i / 1000.0, X: _spring.Ease(i / 1000.0))).ToArray();
        var peak = samples.MaxBy(s => s.X);

        Assert.InRange(peak.X, 1.025, 1.035);
        Assert.InRange(peak.T, 0.57, 0.67);

        var afterPeak = samples.Where(s => s.T > peak.T).Select(s => s.X).ToArray();
        Assert.All(afterPeak, x => Assert.InRange(x, 0.995, peak.X));
        Assert.InRange(_spring.Ease(0.99), 0.995, 1.005);
    }

    [Fact]
    public void RisesMonotonicallyToThePeak()
    {
        double previous = 0;
        for (int i = 1; i <= 600; i++)
        {
            double x = _spring.Ease(i / 1000.0);
            Assert.True(x >= previous, $"dipped at t={i / 1000.0}");
            previous = x;
        }
    }
}
