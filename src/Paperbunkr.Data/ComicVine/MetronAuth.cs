using System.Net.Http.Headers;
using System.Text;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// How a request proves who it is to Metron: the saved API token when there is one, otherwise the legacy username + password
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 1). Every Metron client and factory builds its
/// header here, so switching from a login to a token is one place.
/// <para>
/// Metron takes the token as <c>Authorization: Bearer &lt;token&gt;</c> (confirmed against metron.cloud 2026-10-21: <c>Token</c> got a 401
/// "Authentication credentials were not provided", <c>Bearer</c> returned the issue). Bearer is the default; <see cref="MetronAuthProbe"/>
/// still tries the other scheme on a 401 and saves whichever Metron accepts, in case that ever changes.
/// </para>
/// </summary>
public sealed record MetronAuth(string Scheme, string Parameter)
{
    public const string Provider = "Metron";

    public const string TokenScheme = "Token";

    public const string BearerScheme = "Bearer";

    public bool IsToken => Scheme != "Basic";

    /// <summary>Identifies this credential to <see cref="MetronLoginGuard"/>: a switch from login to token, or to another token, is a new key.</summary>
    public string Key => $"{Scheme} {Parameter}";

    public AuthenticationHeaderValue Header() => new(Scheme, Parameter);

    public static MetronAuth Basic(string username, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

    public static MetronAuth Token(string token, string? scheme = null) =>
        new(NormalizeScheme(scheme), token.Trim());

    /// <summary>The other token scheme, for the probe's second try.</summary>
    public MetronAuth WithOtherScheme() => this with { Scheme = Scheme == BearerScheme ? TokenScheme : BearerScheme };

    /// <summary>The saved scheme, or Bearer (the one Metron accepts) when none was learned.</summary>
    public static string NormalizeScheme(string? scheme) =>
        string.Equals(scheme?.Trim(), TokenScheme, StringComparison.OrdinalIgnoreCase) ? TokenScheme : BearerScheme;

    /// <summary>The saved credential (token first, then the legacy login), or null when neither is saved.</summary>
    public static MetronAuth? Load(PaperbunkrDbContext context)
    {
        var token = CredentialStore.Get(context, Provider, CredentialKind.ApiToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return Token(token, CredentialStore.Get(context, Provider, CredentialKind.AuthScheme));
        }

        var user = CredentialStore.Get(context, Provider, CredentialKind.Username);
        var password = CredentialStore.Get(context, Provider, CredentialKind.Password);
        return string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) ? null : Basic(user, password);
    }

    public static bool IsConfigured(PaperbunkrDbContext context) =>
        CredentialStore.HasCredentials(context, Provider, CredentialKind.ApiToken)
        || CredentialStore.HasCredentials(context, Provider, CredentialKind.Username, CredentialKind.Password);

    public static bool HasToken(PaperbunkrDbContext context) => CredentialStore.HasCredentials(context, Provider, CredentialKind.ApiToken);

    public static bool HasLegacyLogin(PaperbunkrDbContext context) =>
        CredentialStore.HasCredentials(context, Provider, CredentialKind.Username, CredentialKind.Password);
}
