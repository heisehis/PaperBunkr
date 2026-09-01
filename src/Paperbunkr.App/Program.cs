using Avalonia;
using System;
using Paperbunkr.App.Services;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.MaterialDesign;

namespace Paperbunkr.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // First statement, before Avalonia touches anything: a startup failure inside Avalonia's own bootstrap
        // (BuildAvaloniaApp/StartWithClassicDesktopLifetime) needs to be caught too.
        DiagnosticsService.Install();

        // Headless file-association (un)registration, invoked by installer\Installer.iss's optional
        // "associate" task/uninstall step. Deliberately reuses FileAssociationService - the exact
        // same live registry-write path Preferences > Advanced uses - instead of the installer
        // hand-writing ProgID keys itself, so there is only ever one place that knows the current
        // extension list and one owner of those registry keys (see Installer.iss's own file-header
        // note). Must run before Avalonia touches anything, and must exit without ever building a
        // window.
        if (args.Length > 0 && (args[0] == "--register-file-associations" || args[0] == "--unregister-file-associations"))
        {
            bool associate = args[0] == "--register-file-associations";
            var associationService = new FileAssociationService();
            foreach (var format in associationService.GetAvailableFormats())
            {
                associationService.SetAssociated(format.Name, associate);
            }

            return;
        }

        // Icon-font providers for Optris.Icons.Avalonia (the maintained Avalonia 12 fork of
        // Projektanker.Icons.Avalonia) - must be registered before the first <i:Icon> is realized.
        IconProvider.Current
            .Register<FontAwesomeIconProvider>()
            .Register<MaterialDesignIconProvider>();

        // Resolve the rendering backend before Avalonia starts - the graphics stack is chosen
        // inside BuildAvaloniaApp, long before the database is available (docs/superpowers/specs/
        // 2026-08-27-hardware-accelerated-rendering-design.md). Reads the graphics.json cache
        // (mirror of AppSettings) + the PAPERBUNKR_RENDER override.
        var (graphics, source) = GraphicsBootstrap.Resolve();
        DiagnosticsService.LogMilestone(
            $"Render backend requested: {graphics.Backend} preferNativeOpenGl={graphics.PreferNativeOpenGl} (source: {source})");

        try
        {
            BuildAvaloniaApp(graphics).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            DiagnosticsService.LogMilestone("Process exiting.");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer (which calls this
    // parameterless overload by reflection and must not depend on GraphicsBootstrap).
    public static AppBuilder BuildAvaloniaApp()
        => BuildAvaloniaApp(GraphicsConfig.Default);

    public static AppBuilder BuildAvaloniaApp(GraphicsConfig graphics)
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
            // Make the GPU rendering fallback chain explicit rather than relying on Avalonia's
            // implicit Win32 default of [AngleEgl, Software] - Auto adds a native-GL rung before
            // the CPU rasterizer, and Software/Gpu are the escape hatch / no-fallback test mode
            // (spec §4). No-op on non-Windows.
            .With(new Win32PlatformOptions
            {
                RenderingMode = GraphicsBootstrap.ToRenderingModes(graphics),
            })
            .LogToTrace();
}
