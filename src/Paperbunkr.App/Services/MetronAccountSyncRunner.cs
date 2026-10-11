using System;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Builds a <see cref="MetronAccountSync"/> from the saved Metron login and runs it - the one place the scheduled
/// task and the Preferences buttons share (docs/superpowers/specs/2026-10-05-metron-account-sync-design.md).
/// Always at background priority: account sync must never take requests from a scrape the user is waiting on.
/// </summary>
public static class MetronAccountSyncRunner
{
    public const string NoLoginMessage = "No Metron login is saved. Add one under Preferences → Connections.";
    public const string SwitchedOffMessage = "Metron account sync is off. Turn it on under Preferences → Connections.";

    /// <summary>Null when no Metron login is saved.</summary>
    public static MetronAccountSync? Create(Func<PaperbunkrDbContext> createContext)
    {
        string? username, password;
        using (var context = createContext())
        {
            username = CredentialStore.Get(context, "Metron", CredentialKind.Username);
            password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var client = new MetronClient(username, password, ComicVineRequestPriority.Low);
        return new MetronAccountSync(createContext, new MetronAccountClient(client), client);
    }

    /// <summary>
    /// The scheduled task's body: one line for the Activity Center. A run stopped by its budget or the rate limit is a normal result; a refused login or
    /// an outage throws, which is how a scheduled task reports failure.
    /// </summary>
    public static async Task<string> RunScheduledAsync(Func<PaperbunkrDbContext> createContext, IActivityJobHandle? handle, CancellationToken cancellationToken)
    {
        using (var context = createContext())
        {
            if (!context.GetOrCreateAppSettings().MetronSyncEnabled)
            {
                return SwitchedOffMessage;
            }
        }

        if (Create(createContext) is not { } sync)
        {
            return NoLoginMessage;
        }

        handle?.Report("Syncing with Metron…");
        var report = await sync.RunAsync(cancellationToken).ConfigureAwait(false);
        return report.Failed ? throw new InvalidOperationException(report.Summary) : report.Summary;
    }
}
