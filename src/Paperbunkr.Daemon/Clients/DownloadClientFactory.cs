using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Clients;

/// <summary>Builds the configured download client from the acquisition settings and the encrypted credentials.</summary>
public static class DownloadClientFactory
{
    public const string CredentialProvider = "qBittorrent";

    /// <summary>The client, or <c>null</c> when no qBittorrent address or category is configured (so "not set up" is a normal state, not an error).</summary>
    public static IDownloadClient? Create(PaperbunkrDbContext context)
    {
        var settings = context.GetOrCreateAcquisitionSettings();
        if (string.IsNullOrWhiteSpace(settings.QBittorrentUrl) || string.IsNullOrWhiteSpace(settings.QBittorrentCategory))
        {
            return null;
        }

        // Empty credentials are legitimate: qBittorrent can be set to skip authentication for localhost.
        var username = CredentialStore.Get(context, CredentialProvider, CredentialKind.Username) ?? string.Empty;
        var password = CredentialStore.Get(context, CredentialProvider, CredentialKind.Password) ?? string.Empty;
        return new QBittorrentClient(settings.QBittorrentUrl, username, password, settings.QBittorrentCategory);
    }
}
