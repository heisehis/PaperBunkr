using Avalonia;
using System;

namespace Paperbunkr.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            // Default GPU resource cache (~28MB) is trivial for comic/webtoon pages (docs/
            // onboarding.md §8, docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-
            // chrome-overlays-design.md §3) - 384MB is the middle of the spec's suggested
            // 256-512MB desktop-default range. Confirmed the real type is `Avalonia.SkiaOptions`
            // (not `Avalonia.Skia.SkiaOptions`) via reflection against the built app's own
            // Avalonia.Skia.dll, not guessed.
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 384L * 1024 * 1024 })
            .LogToTrace();
}
