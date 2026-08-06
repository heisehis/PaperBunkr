using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Minimal headless Avalonia bootstrap, run once per test assembly via
/// <see cref="AvaloniaTestCollection"/> - needed because
/// <see cref="Avalonia.Media.Imaging.Bitmap"/> construction requires a registered
/// IPlatformRenderInterface, which only exists once an AppBuilder has run. Bootstraps directly
/// (SetupWithoutStarting) with plain [Fact] tests rather than [AvaloniaFact] - the latter wasn't
/// being discovered by the xunit runner in this setup (0 tests found despite the assembly loading
/// fine) and wasn't worth chasing further for what's a thin, one-time platform bootstrap.
/// </summary>
public static class TestAppBuilder
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // UseHeadlessDrawing defaults to true (stubs out real rendering entirely, fine for pure
        // UI-layout tests) - disabled here since these tests need real Skia-backed image decode,
        // confirmed necessary after the default produced a silently-wrong 1x1 Bitmap for a real
        // 64x96 PNG with no exception thrown.
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        _initialized = true;
    }
}

/// <summary>Ensures <see cref="TestAppBuilder.EnsureInitialized"/> runs once before any test in this collection.</summary>
[CollectionDefinition(nameof(AvaloniaTestCollection))]
public class AvaloniaTestCollection : ICollectionFixture<AvaloniaTestCollection>
{
    public AvaloniaTestCollection() => TestAppBuilder.EnsureInitialized();
}
