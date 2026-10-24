namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Test connection for Metron (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md, "Scheme probing"): sends one
/// cheap request with the saved credential and, when a token is refused with a 401, tries the other token scheme once. The scheme
/// Metron accepts is handed back so the caller can save it. At most two rejected requests, which <see cref="MetronLoginGuard"/> allows.
/// </summary>
public static class MetronAuthProbe
{
    public sealed record Result(bool Accepted, MetronAuth? Working, string Message);

    public static Task<Result> ProbeAsync(MetronAuth auth, CancellationToken cancellationToken) =>
        ProbeAsync(auth, (a, ct) => new MetronClient(a).VerifyAsync(ct), cancellationToken);

    /// <summary><paramref name="verify"/> throws <see cref="ComicVineException"/> (code 100) when Metron refuses the credential.</summary>
    public static async Task<Result> ProbeAsync(MetronAuth auth, Func<MetronAuth, CancellationToken, Task> verify, CancellationToken cancellationToken)
    {
        var first = await TryAsync(auth, verify, cancellationToken).ConfigureAwait(false);
        if (first is null)
        {
            return new Result(true, auth, auth.IsToken ? $"Metron accepted the API token ({auth.Scheme} scheme)." : "Metron accepted the login.");
        }

        if (!auth.IsToken || first.HttpStatus != 401)
        {
            return new Result(false, null, first.Message);
        }

        var other = auth.WithOtherScheme();
        var second = await TryAsync(other, verify, cancellationToken).ConfigureAwait(false);
        return second is null
            ? new Result(true, other, $"Metron accepted the API token ({other.Scheme} scheme).")
            : new Result(false, null, "Metron rejected the API token with both the Token and Bearer schemes. Check that it was copied whole.");
    }

    private static async Task<ComicVineException?> TryAsync(MetronAuth auth, Func<MetronAuth, CancellationToken, Task> verify, CancellationToken cancellationToken)
    {
        try
        {
            await verify(auth, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (ComicVineException ex)
        {
            return ex;
        }
    }
}
