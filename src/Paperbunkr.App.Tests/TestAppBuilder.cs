using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Xunit;
using Xunit.Abstractions;

// Root cause (confirmed via full stack trace, not just the outer exception): AppBuilder.Setup()
// itself throws `InvalidOperationException: The calling thread cannot access this object because a
// different thread owns it` from:
//   Dispatcher.VerifyAccess() <- DefaultRenderLoop.Add() <- ServerCompositor..ctor()
//   <- Compositor..ctor() <- AvaloniaHeadlessPlatform.Initialize() <- AppBuilder.SetupUnsafe()
// Dispatcher.UIThread is a process-wide singleton that binds to whichever thread first touches it.
// Setup()'s own compositor construction calls VerifyAccess() against it - so this only throws if
// something ELSE already created/bound Dispatcher.UIThread, on a DIFFERENT thread, before
// AvaloniaTestCollection's fixture constructor got its turn to call Setup(). That "something else"
// is any of the ~50 test classes in this assembly that don't opt into
// [Collection(nameof(AvaloniaTestCollection))] (only ones built around real ViewModels/services
// need it) - several of them exercise production code that itself posts through
// Dispatcher.UIThread (e.g. toast/notification marshaling), which is enough to claim the thread.
// xUnit's default parallel-collections scheduling means the exact interleaving of "which untagged
// collection's worker thread gets there first" is nondeterministic - confirmed via repro: ~3 of 8
// full-suite runs hit this, cascading into ~1000 reported failures (every test depending on the one
// fixture instance that failed to construct).
//
// DisableTestParallelization alone does NOT fix this: it stops collections from running
// *concurrently*, but doesn't control *which collection runs first* - an untagged collection can
// still be scheduled and start (and claim Dispatcher.UIThread) before AvaloniaTestCollection's
// collection gets its turn, even when nothing overlaps. Confirmed by re-reproducing the identical
// crash with DisableTestParallelization already applied. The AvaloniaFirstCollectionOrderer below
// closes that gap by forcing AvaloniaTestCollection to be scheduled first, always - so nothing else
// in the assembly can touch Dispatcher.UIThread before Setup() has bound it. DisableTestParallelization
// is kept anyway as defense in depth (also matches Avalonia's own guidance for reusing one
// Application/Dispatcher across an assembly's tests - their xUnit integration's `PerAssembly`
// isolation level - "Concurrent test execution is not supported").
//
// Not a [ModuleInitializer]: that runs Setup() on a one-off assembly-load thread that xUnit's
// worker-thread pool never reuses for actual test execution, which breaks the *other* half of this
// contract - tests like BulkIssuePropertiesWriteBackTests poll Dispatcher.UIThread.CheckAccess()
// from whatever thread is running them and only pump the queue when it's true (see
// IssuePropertiesWriteBackTests.WaitUntilAsync's doc comment for the same idiom); AsyncCoverImage's
// completion callback posts back through that same dispatcher. That only works if Setup() ran on a
// thread the pool goes on to reuse for real test methods - confirmed: forcing Setup() onto a
// module-init thread instead made every AsyncCoverImage post-back fail with the identical
// cross-thread exception, deterministically, 8/8 runs. The collection-fixture constructor (an
// actual xUnit worker thread) plus ordering it first is what satisfies both constraints at once.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCollectionOrderer(
    "Paperbunkr.App.Tests.AvaloniaFirstCollectionOrderer", "Paperbunkr.App.Tests")]

namespace Paperbunkr.App.Tests;

/// <summary>
/// Forces <see cref="AvaloniaTestCollection"/> to be the first collection xUnit schedules, so
/// nothing else in the assembly can touch <c>Dispatcher.UIThread</c> before
/// <see cref="TestAppBuilder.EnsureInitialized"/> has bound it. See the assembly-level comment
/// above this file's <c>CollectionBehavior</c>/<c>TestCollectionOrderer</c> attributes for the full
/// mechanism this closes.
/// </summary>
public class AvaloniaFirstCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderByDescending(c => c.DisplayName == nameof(AvaloniaTestCollection));
}

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
    private static readonly object InitLock = new();

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // Defense in depth alongside the assembly-level DisableTestParallelization above: that
        // stops *concurrent* collections from racing this, but doesn't by itself make this method
        // safe to call reentrantly/concurrently if that assumption is ever weakened later.
        lock (InitLock)
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
}

/// <summary>Ensures <see cref="TestAppBuilder.EnsureInitialized"/> runs once before any test in this collection.</summary>
[CollectionDefinition(nameof(AvaloniaTestCollection))]
public class AvaloniaTestCollection : ICollectionFixture<AvaloniaTestCollection>
{
    public AvaloniaTestCollection()
    {
        TestAppBuilder.EnsureInitialized();

        // Process-wide safety net: some tests (MainViewModel's ctor reconcile, LibraryScreen's
        // background regen, migration hooks) reach the cover services on a fire-and-forget thread
        // whose lifetime a per-test fixture can't bracket. Without this, that sweep attics the real
        // per-user cover cache. Set once for the whole run and never restored; per-test fixtures
        // that need their own isolated dir still override it and restore to this temp base.
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paperbunkr_covers_global_{System.Guid.NewGuid():N}");
        Paperbunkr.App.Services.CoverThumbnailPaths.ThumbnailDirectory = System.IO.Path.Combine(root, "thumbnails");
        Paperbunkr.App.Services.BookCoverThumbnailPaths.ThumbnailDirectory = System.IO.Path.Combine(root, "book-thumbnails");
        Paperbunkr.App.Services.Covers.CustomCoverPaths.Directory = System.IO.Path.Combine(root, "custom-covers");
        Paperbunkr.App.Services.Covers.CustomBookCoverPaths.Directory = System.IO.Path.Combine(root, "custom-book-covers");
        Paperbunkr.App.Services.Covers.CoverCacheState.FilePath = System.IO.Path.Combine(root, "cover-cache-state.json");
    }
}
