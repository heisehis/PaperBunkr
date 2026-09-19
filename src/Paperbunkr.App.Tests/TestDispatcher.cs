using System;
using Avalonia.Threading;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Drains <c>Dispatcher.UIThread</c> in headless tests, where nothing runs a dispatcher loop, so
/// view-model code that defers work with <c>Dispatcher.UIThread.Post</c> (every delete/close path,
/// see CLAUDE.md "don't remove/detach a control from inside a routed event") only executes when a
/// test pumps it. Unlike the old per-class guards this never silently no-ops: if the calling thread
/// isn't the one that bootstrapped Avalonia it throws, because a skipped pump makes the following
/// assertion fail (or worse, pass) for the wrong reason. <see cref="PinnedThreadTestFramework"/>
/// guarantees the right thread; this is the tripwire if that ever regresses.
/// </summary>
public static class TestDispatcher
{
    public static void Drain()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                $"TestDispatcher.Drain() called on thread {Environment.CurrentManagedThreadId}, which does not own " +
                "Dispatcher.UIThread. Tests must run on the bootstrap thread (see PinnedThreadTestFramework).");
        }

        Dispatcher.UIThread.RunJobs();
    }
}
