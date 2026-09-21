using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Acquisition;

/// <summary>
/// The weekly pull list's source, chosen by what is saved: Metron when its login is (it lists by date across all publishers), otherwise ComicVine when its key is, otherwise none.
/// The same choice the background cycle makes, exposed so the Releases tab can fetch a week on demand.
/// </summary>
public static class PullListSourceFactory
{
    public static IPullListSource? Create(PaperbunkrDbContext context, ComicVineRequestPriority priority = ComicVineRequestPriority.High)
    {
        var user = CredentialStore.Get(context, "Metron", CredentialKind.Username);
        var password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(password))
        {
            return new MetronClient(user, password, priority);
        }

        var apiKey = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        return string.IsNullOrWhiteSpace(apiKey) ? null : new ComicVineClient(apiKey, priority);
    }
}
