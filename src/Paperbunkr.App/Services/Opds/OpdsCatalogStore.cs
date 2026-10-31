using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Opds;

/// <summary>Add, edit, remove and read OPDS catalogs; the password is DPAPI-protected at rest and fails closed where DPAPI is unavailable.</summary>
public sealed class OpdsCatalogStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("paperbunkr.opds.catalog.v1");

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public OpdsCatalogStore(Func<PaperbunkrDbContext>? contextFactory = null) =>
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;

    public IReadOnlyList<OpdsCatalog> List()
    {
        using var context = _contextFactory();
        return context.OpdsCatalogs.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
    }

    /// <summary>The address as the catalog needs it: absolute http or https, "https://" added when the user typed none; null when it cannot be one.</summary>
    public static string? NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string text = raw.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) ? uri.AbsoluteUri : null;
    }

    public static bool IsPlainHttp(string url) => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Saves a new catalog (<paramref name="id"/> null) or updates one. An empty password on an update keeps the stored one.</summary>
    public OpdsCatalog Save(int? id, string name, string url, string? username, string? password)
    {
        string? normalized = NormalizeUrl(url) ?? throw new ArgumentException("That is not a web address.", nameof(url));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Give the catalog a name.", nameof(name));
        }

        using var context = _contextFactory();
        OpdsCatalog catalog;
        if (id is int existing)
        {
            catalog = context.OpdsCatalogs.Single(c => c.Id == existing);
        }
        else
        {
            catalog = new OpdsCatalog { AddedTime = DateTime.UtcNow, SortOrder = context.OpdsCatalogs.Any() ? context.OpdsCatalogs.Max(c => c.SortOrder) + 1 : 0 };
            context.OpdsCatalogs.Add(catalog);
        }

        catalog.Name = name.Trim();
        catalog.Url = normalized;
        catalog.Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        if (catalog.Username is null)
        {
            catalog.ProtectedPassword = null;
        }
        else if (!string.IsNullOrEmpty(password))
        {
            if (!DpapiSecrets.IsSupported)
            {
                throw new PlatformNotSupportedException("Passwords can only be saved on Windows, where they are encrypted for your account.");
            }

            catalog.ProtectedPassword = DpapiSecrets.Protect(password, Entropy);
        }

        context.SaveChanges();
        return catalog;
    }

    public void Remove(int id)
    {
        using var context = _contextFactory();
        var catalog = context.OpdsCatalogs.FirstOrDefault(c => c.Id == id);
        if (catalog is null)
        {
            return;
        }

        context.OpdsDownloads.RemoveRange(context.OpdsDownloads.Where(d => d.CatalogId == id));
        context.OpdsCatalogs.Remove(catalog);
        context.SaveChanges();
    }

    public static string? PasswordOf(OpdsCatalog catalog) =>
        string.IsNullOrEmpty(catalog.ProtectedPassword) ? null : DpapiSecrets.TryUnprotect(catalog.ProtectedPassword, Entropy);

    public static OpdsCredentials? CredentialsOf(OpdsCatalog catalog) => OpdsClient.CredentialsFor(catalog, PasswordOf(catalog));
}
