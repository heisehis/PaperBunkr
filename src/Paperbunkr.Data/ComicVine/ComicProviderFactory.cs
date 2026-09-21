using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Builds a <see cref="IComicProvider"/> from the credentials saved under Preferences → Connections (the ComicVine key, or the Metron login), and says what is missing when there isn't
/// one. Every place that talks to a provider goes through here, so adding a credential in Connections is all a provider needs.
/// </summary>
public static class ComicProviderFactory
{
    public static readonly IReadOnlyList<ComicProvider> All = new[] { ComicProvider.ComicVine, ComicProvider.Metron };

    public static string DisplayName(ComicProvider provider) => provider == ComicProvider.Metron ? "Metron" : "ComicVine";

    public static ComicProvider Parse(string? text) =>
        string.Equals(text?.Trim(), "Metron", StringComparison.OrdinalIgnoreCase) ? ComicProvider.Metron : ComicProvider.ComicVine;

    /// <summary>The provider, or <c>null</c> when its credentials aren't saved.</summary>
    public static IComicProvider? Create(PaperbunkrDbContext context, ComicProvider provider, ComicVineRequestPriority priority = ComicVineRequestPriority.High)
    {
        if (provider == ComicProvider.Metron)
        {
            var user = CredentialStore.Get(context, "Metron", CredentialKind.Username);
            var password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
            return string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) ? null : new MetronClient(user, password, priority);
        }

        var key = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        return string.IsNullOrWhiteSpace(key) ? null : new ComicVineClient(key, priority);
    }

    public static bool IsAvailable(PaperbunkrDbContext context, ComicProvider provider) => Create(context, provider) is not null;

    /// <summary>What to tell the user when <see cref="Create"/> returned null.</summary>
    public static string MissingCredentialsMessage(ComicProvider provider) => provider == ComicProvider.Metron
        ? "Add your Metron login under Preferences → Connections first."
        : "Add your ComicVine API key under Preferences → Connections first.";
}
