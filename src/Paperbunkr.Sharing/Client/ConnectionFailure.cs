using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Paperbunkr.Sharing.Client;

/// <summary>
/// Turns a failed connection into a sentence that says what actually happened and what to check. Before this, every failure read
/// "Could not reach host:port." - a firewall drop, a refused port, a TLS failure and a 403 all looked the same, so a user on the
/// other computer had nothing to go on.
/// </summary>
public static class ConnectionFailure
{
    /// <summary>No reply before the timeout: almost always a firewall silently dropping the connection, or an address nobody answers on.</summary>
    public static string NoAnswer(string target) =>
        $"No answer from {target}. Usually Windows Firewall on the sharing computer is blocking Paperbunkr, " +
        "or this is not that computer's address on your network. Check the address shown in its Sharing settings.";

    public static string ForStatus(string target, HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden =>
            $"{target} turned this computer away: it only accepts computers on a private network or VPN.",
        HttpStatusCode.NotFound =>
            $"{target} answered, but not as a Paperbunkr library. Check the port number.",
        _ => $"{target} answered with error {(int)status}.",
    };

    public static string Describe(string target, HttpRequestException ex)
    {
        if (ex.StatusCode is HttpStatusCode status)
        {
            return ForStatus(target, status);
        }

        if (Find<AuthenticationException>(ex) is not null)
        {
            return $"Reached {target}, but the secure connection failed. Check that the port belongs to Paperbunkr's sharing.";
        }

        if (Find<SocketException>(ex) is { } socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused =>
                    $"{target} refused the connection. Check that sharing is turned on there and the port number is right.",
                SocketError.TimedOut => NoAnswer(target),
                SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown =>
                    $"This computer has no route to {target}. Both computers need to be on the same network (or VPN).",
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
                    $"Couldn't find a computer called {target}. Try its IP address instead.",
                _ => $"Could not reach {target} ({socket.SocketErrorCode}).",
            };
        }

        return $"Could not reach {target}.";
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is T match)
            {
                return match;
            }
        }

        return null;
    }
}
