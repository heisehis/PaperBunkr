using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Paperbunkr.Sharing.Client;
using Xunit;

namespace Paperbunkr.Sharing.Tests;

public class ConnectionFailureTests
{
    private const string Target = "192.168.1.20:7614";

    private static HttpRequestException Wrap(Exception inner) => new("failed", inner);

    [Theory]
    [InlineData(SocketError.ConnectionRefused, "refused the connection")]
    [InlineData(SocketError.TimedOut, "Windows Firewall")]
    [InlineData(SocketError.HostUnreachable, "no route to")]
    [InlineData(SocketError.NetworkUnreachable, "no route to")]
    [InlineData(SocketError.HostNotFound, "Try its IP address")]
    public void SocketErrors_SayWhatHappened(SocketError error, string expected)
    {
        string message = ConnectionFailure.Describe(Target, Wrap(new SocketException((int)error)));

        Assert.Contains(expected, message);
        Assert.Contains(Target, message);
    }

    [Fact]
    public void SocketError_FoundDeeperInTheChain()
    {
        var nested = Wrap(new IOException("io", new SocketException((int)SocketError.ConnectionRefused)));

        Assert.Contains("refused the connection", ConnectionFailure.Describe(Target, nested));
    }

    [Fact]
    public void TlsFailure_SaysTheSecureConnectionFailed() =>
        Assert.Contains("secure connection failed", ConnectionFailure.Describe(Target, Wrap(new AuthenticationException())));

    [Fact]
    public void Forbidden_SaysOnlyPrivateNetworksAreAccepted() =>
        Assert.Contains("private network", ConnectionFailure.Describe(Target, new HttpRequestException("x", null, HttpStatusCode.Forbidden)));

    [Fact]
    public void OtherStatus_ShowsTheCode() =>
        Assert.Contains("error 500", ConnectionFailure.ForStatus(Target, HttpStatusCode.InternalServerError));

    [Fact]
    public void NoAnswer_PointsAtTheFirewallAndTheAddress()
    {
        string message = ConnectionFailure.NoAnswer(Target);

        Assert.Contains("Windows Firewall", message);
        Assert.Contains("address", message);
    }

    [Fact]
    public void UnknownFailure_StillNamesTheTarget() =>
        Assert.Equal($"Could not reach {Target}.", ConnectionFailure.Describe(Target, new HttpRequestException("x")));
}
