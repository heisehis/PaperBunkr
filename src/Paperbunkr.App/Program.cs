using Avalonia;
using System;
using System.Linq;
using System.Threading;
using Paperbunkr.App.Services;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using Optris.Icons.Avalonia.MaterialDesign;

namespace Paperbunkr.App;

sealed class Program
{
    // Held for the whole process lifetime (never disposed) purely so the installer's
    // AppMutex check (installer/Installer.iss) can tell Paperbunkr is running and prompt the user
    // to close it before Setup/Uninstall touches any files. NOT single-instance enforcement - the
    // app still allows multiple windows; this mutex just has to *exist* while any instance is up.
    // The name MUST stay identical to Installer.iss's AppMutex value.
    private const string RunningMutexName = "Paperbunkr_App_Running";
    private static Mutex? _runningMutex;

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
        // per-format "associate*" tasks/uninstall steps. Deliberately reuses FileAssociationService
        // - the exact same live registry-write path Preferences > Advanced uses - instead of the
        // installer hand-writing ProgID keys itself, so there is only ever one place that knows the
        // current extension list and one owner of those registry keys (see Installer.iss's own
        // file-header note). Must run before Avalonia touches anything, and must exit without ever
        // building a window.
        //
        // Trailing args are the specific extensions to act on (".cbz", ".pdf", ...); the installer
        // passes one per selected task. With no extensions given, defaults to the full comic-format
        // allow-list. Either way the set is clamped to FileAssociationService.ComicAssociationExtensions
        // - so this path never (re)associates bare .zip/.rar/.7z, unlike the old
        // "loop every GetAvailableFormats() entry" it replaces (2026-09-09 installer redesign, decision 5).
        if (args.Length > 0 && (args[0] == "--register-file-associations" || args[0] == "--unregister-file-associations"))
        {
            bool associate = args[0] == "--register-file-associations";
            var requestedExtensions = args.Length > 1
                ? args[1..]
                : FileAssociationService.ComicAssociationExtensions.ToArray();
            new FileAssociationService().SetComicAssociationsFor(requestedExtensions, associate);

            return;
        }

        // Create the installer-detection mutex only on the real GUI path (after the headless
        // file-association early-return above, which the installer itself invokes mid-install).
        // Global\ so an elevated Setup process in a different token still sees it; fall back to a
        // session-local name if the Global namespace is denied. Best-effort - nothing in-process
        // depends on it.
        try
        {
            _runningMutex = new Mutex(initiallyOwned: false, @"Global\" + RunningMutexName);
        }
        catch (Exception)
        {
            try { _runningMutex = new Mutex(initiallyOwned: false, RunningMutexName); }
            catch (Exception) { /* give up - the installer still has CloseApplications as a fallback */ }
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
            $"Render backend requested: {graphics.Backend} preferNativeOpenGl={graphics.PreferNativeOpenGl} (source: {source}); "
            + $"rendering-mode chain [{string.Join(", ", GraphicsBootstrap.ToRenderingModes(graphics))}]; "
            + "composition [WinUIComposition, DirectComposition, RedirectionSurface]");

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
                // Prefer the GPU-composited, vsync-locked present paths and keep RedirectionSurface -
                // which can tear, and which is what Avalonia's default [WinUIComposition,
                // RedirectionSurface] drops straight to when WinUI composition isn't available - only
                // as the last resort. WinUIComposition needs the WinRT Windows.UI.Composition APIs,
                // which some Windows editions (Server, and certain LTSC/IoT images - this app has
                // shipped on "IoT Enterprise LTSC") don't include; DirectComposition is the rawer
                // DComp path present on Win8+ regardless, so it sits between them. All three
                // composited modes require RenderingMode to resolve to AngleEgl - on a Wgl/Software
                // fallback they're ignored and RedirectionSurface is used anyway (docs/superpowers/
                // specs/2026-08-27-hardware-accelerated-rendering-design.md §4).
                CompositionMode = new[]
                {
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface,
                },
            })
            .LogToTrace();
}
