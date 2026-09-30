using System;
using System.Threading.Tasks;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// What the Continuity screen's pages may ask of the shell and the app (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-
/// design.md, "Architecture"): open things, read, notify, delete, and tell the sidebar something changed. Built once by
/// <see cref="ContinuityScreenViewModel"/> with lambdas over its own state, so a page never holds the shell.
/// </summary>
public sealed class ContinuityScreenNavigation
{
    public Action<int> GoToSeriesDetail { get; init; } = _ => { };

    public Action<int> GoToReader { get; init; } = _ => { };

    /// <summary>Opens the reader anchored to a story event's order (issue id, event id).</summary>
    public Action<int, int> GoToReaderInEvent { get; init; } = (_, _) => { };

    public Action<int> GoToReadingList { get; init; } = _ => { };

    public Action<string, string> Notify { get; init; } = (_, _) => { };

    public IActivityService Activity { get; init; } = new ActivityService();

    public Action<int> OpenEvent { get; init; } = _ => { };

    /// <summary>Opens an event straight on its Map tab.</summary>
    public Action<int> OpenEventMap { get; init; } = _ => { };

    public Action<int> OpenContinuity { get; init; } = _ => { };

    /// <summary>Opens Suggestions &amp; checks, optionally at the duplicate pair involving this event.</summary>
    public Action<int?> OpenSuggestions { get; init; } = _ => { };

    public Action<int> DeleteEvent { get; init; } = _ => { };

    public Action<int> DeleteContinuity { get; init; } = _ => { };

    /// <summary>Names or counts changed - the sidebar lists refresh.</summary>
    public Action SidebarChanged { get; init; } = () => { };

    /// <summary>How the pages' heavy overview query leaves the UI thread. Tests pass a synchronous runner.</summary>
    public Func<Func<object?>, Task<object?>> Runner { get; init; } = work => Task.Run(work);

    /// <summary>Opens the installed Grand Comics Database extract, or null without one. Tests pass <c>() => null</c>.</summary>
    public Func<Paperbunkr.Data.Gcd.GcdDataStore?> OpenGcd { get; init; } = () => Paperbunkr.Data.Gcd.GcdDataStore.TryOpen();
}
