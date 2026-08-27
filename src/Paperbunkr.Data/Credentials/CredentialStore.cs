using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Credentials;

/// <summary>
/// Thin wrapper over <see cref="ProviderCredential"/> (docs/superpowers/specs/2026-08-22-cbl-
/// manager-arc-lookup-design.md §2) - the single place reading-list source adapters (and, later,
/// tracker-sync adapters) read/write their secrets, instead of touching the DbSet directly.
/// </summary>
public static class CredentialStore
{
    public static string? Get(PaperbunkrDbContext context, string provider, CredentialKind kind)
    {
        return context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind)?.Value;
    }

    public static void Set(PaperbunkrDbContext context, string provider, CredentialKind kind, string value)
    {
        var existing = context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind);
        if (existing is null)
        {
            context.ProviderCredentials.Add(new ProviderCredential
            {
                Provider = provider,
                Kind = kind,
                Value = value,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        context.SaveChanges();
    }

    public static void Delete(PaperbunkrDbContext context, string provider, CredentialKind kind)
    {
        var existing = context.ProviderCredentials
            .FirstOrDefault(c => c.Provider == provider && c.Kind == kind);
        if (existing is not null)
        {
            context.ProviderCredentials.Remove(existing);
            context.SaveChanges();
        }
    }

    /// <summary>True when every one of <paramref name="required"/> has a non-empty stored value for <paramref name="provider"/>.</summary>
    public static bool HasCredentials(PaperbunkrDbContext context, string provider, params CredentialKind[] required)
    {
        var stored = context.ProviderCredentials
            .Where(c => c.Provider == provider)
            .ToList();

        return required.All(kind => stored.Any(c => c.Kind == kind && !string.IsNullOrEmpty(c.Value)));
    }
}
