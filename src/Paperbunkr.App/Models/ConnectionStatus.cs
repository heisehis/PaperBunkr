using System;

namespace Paperbunkr.App.Models;

/// <summary>Which captioned tile group of the Connections screen a <see cref="ConnectionProviderRow"/> belongs to (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md).</summary>
public enum ConnectionGroup
{
    Metadata,
    Trackers,
    Downloads,
}

/// <summary>What a tile shows next to its dot. <see cref="Info"/> is for tiles that are not accounts (the tracker Behavior tile).</summary>
public enum ConnectionStatus
{
    NotSetUp,
    Connected,
    Syncing,

    /// <summary>The service is set up but rejected the saved sign-in at the last check.</summary>
    Expired,

    /// <summary>The service is set up but could not be reached at the last check.</summary>
    Unreachable,

    Info,
}

/// <summary>Why a check failed. Drives <see cref="ConnectionStatus.Expired"/> versus <see cref="ConnectionStatus.Unreachable"/>.</summary>
public enum ConnectionFailure
{
    None,
    Auth,
    Unreachable,
}

/// <summary>The outcome of the last explicit check of one provider, persisted by <c>ConnectionStatusStore</c>.</summary>
public sealed record ConnectionCheck(bool Ok, ConnectionFailure Failure, DateTimeOffset CheckedUtc, string? Detail)
{
    public static ConnectionCheck Success(DateTimeOffset now, string? detail = null) => new(true, ConnectionFailure.None, now, detail);

    public static ConnectionCheck Failed(ConnectionFailure failure, DateTimeOffset now, string? detail) =>
        new(false, failure == ConnectionFailure.None ? ConnectionFailure.Unreachable : failure, now, detail);
}

/// <summary>The one place that turns "is it connected, how did the last check go, is sync running" into a <see cref="ConnectionStatus"/> (pure, so it is unit-tested directly).</summary>
public static class ConnectionStatusResolver
{
    public static ConnectionStatus Resolve(bool isConnected, ConnectionCheck? lastCheck, bool isSyncing)
    {
        if (!isConnected)
        {
            return ConnectionStatus.NotSetUp;
        }

        if (lastCheck is { Ok: false })
        {
            return lastCheck.Failure == ConnectionFailure.Auth ? ConnectionStatus.Expired : ConnectionStatus.Unreachable;
        }

        return isSyncing ? ConnectionStatus.Syncing : ConnectionStatus.Connected;
    }

    public static string DefaultText(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "Connected",
        ConnectionStatus.Syncing => "Syncing",
        ConnectionStatus.Expired => "Needs sign-in",
        ConnectionStatus.Unreachable => "Unreachable",
        ConnectionStatus.Info => string.Empty,
        _ => "Not set up",
    };

    /// <summary>"never", "just now", "12 min ago", "3 h ago", "2 d ago".</summary>
    public static string AgeText(DateTimeOffset? checkedUtc, DateTimeOffset now)
    {
        if (checkedUtc is not { } at)
        {
            return "never";
        }

        var age = now - at;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} min ago";
        }

        return age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours} h ago" : $"{(int)age.TotalDays} d ago";
    }
}
