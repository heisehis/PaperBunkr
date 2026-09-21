using System.Net;
using Microsoft.Extensions.Time.Testing;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Tests;

public class AuthPrimitivesTests
{
    // Low iteration count keeps the suite fast; the format and comparison logic under test don't depend on it.
    private const int FastIterations = 1_000;

    [Fact]
    public void PasswordHasher_VerifiesCorrectPassword_RejectsWrongOne()
    {
        string stored = PasswordHasher.Hash("correct horse", FastIterations);

        Assert.True(PasswordHasher.Verify("correct horse", stored));
        Assert.False(PasswordHasher.Verify("wrong horse", stored));
        Assert.False(PasswordHasher.Verify("", stored));
    }

    [Fact]
    public void PasswordHasher_StoredValue_NeverContainsThePlaintext_AndIsSalted()
    {
        string a = PasswordHasher.Hash("hunter2hunter2", FastIterations);
        string b = PasswordHasher.Hash("hunter2hunter2", FastIterations);

        Assert.DoesNotContain("hunter2hunter2", a);
        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha256$1000$", a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$!!!$!!!")]
    [InlineData("md5$1000$AAAA$AAAA")]
    public void PasswordHasher_MalformedStoredValue_VerifiesFalse_WithoutThrowing(string stored)
    {
        Assert.False(PasswordHasher.Verify("anything", stored));
    }

    [Fact]
    public void SessionStore_ValidatesLiveToken_AndSlidesExpiry()
    {
        var time = new FakeTimeProvider();
        var store = new SessionStore(TimeSpan.FromMinutes(10), time);
        var (token, _) = store.Create();

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(store.TryValidate(token));      // slides: now expires 10 min from here

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.True(store.TryValidate(token));      // would have expired without the slide

        time.Advance(TimeSpan.FromMinutes(11));
        Assert.False(store.TryValidate(token));
    }

    [Fact]
    public void SessionStore_RejectsUnknownEmptyAndRevokedTokens()
    {
        var store = new SessionStore(TimeSpan.FromMinutes(10));
        var (token, _) = store.Create();

        Assert.False(store.TryValidate(null));
        Assert.False(store.TryValidate(""));
        Assert.False(store.TryValidate("not-a-token"));

        store.Revoke(token);
        Assert.False(store.TryValidate(token));
    }

    [Fact]
    public void SessionStore_TokensAreUniqueAndLong()
    {
        var store = new SessionStore(TimeSpan.FromMinutes(10));

        var tokens = Enumerable.Range(0, 50).Select(_ => store.Create().Token).ToList();

        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.True(t.Length >= 42)); // 32 random bytes, base64url
    }

    [Fact]
    public void FailedAuthLimiter_LocksOutAfterMaxFailures_AndRecoversAfterTheDuration()
    {
        var time = new FakeTimeProvider();
        var limiter = new FailedAuthLimiter(maxFailures: 3, baseLockout: TimeSpan.FromMinutes(1), time: time);
        string? lockedKey = null;
        limiter.LockedOut += k => lockedKey = k;

        limiter.RecordFailure("10.0.0.5");
        limiter.RecordFailure("10.0.0.5");
        Assert.False(limiter.IsBlocked("10.0.0.5", out _));
        limiter.RecordFailure("10.0.0.5");

        Assert.True(limiter.IsBlocked("10.0.0.5", out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero && retryAfter <= TimeSpan.FromMinutes(1));
        Assert.Equal("10.0.0.5", lockedKey);
        Assert.False(limiter.IsBlocked("10.0.0.6", out _)); // per-client

        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.False(limiter.IsBlocked("10.0.0.5", out _));
    }

    [Fact]
    public void FailedAuthLimiter_SecondLockoutIsLonger()
    {
        var time = new FakeTimeProvider();
        var limiter = new FailedAuthLimiter(maxFailures: 1, baseLockout: TimeSpan.FromMinutes(1), maxLockout: TimeSpan.FromMinutes(15), time: time);

        limiter.RecordFailure("c");
        Assert.True(limiter.IsBlocked("c", out TimeSpan first));
        time.Advance(first + TimeSpan.FromSeconds(1));

        limiter.RecordFailure("c");
        Assert.True(limiter.IsBlocked("c", out TimeSpan second));

        Assert.True(second > first);
    }

    [Fact]
    public void FailedAuthLimiter_SuccessClearsTheRecord()
    {
        var limiter = new FailedAuthLimiter(maxFailures: 3);
        limiter.RecordFailure("c");
        limiter.RecordFailure("c");

        limiter.RecordSuccess("c");
        limiter.RecordFailure("c");
        limiter.RecordFailure("c");

        Assert.False(limiter.IsBlocked("c", out _)); // counter restarted, only 2 since the success
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.20", true)]
    [InlineData("169.254.10.10", true)]
    [InlineData("100.64.0.1", true)]     // Tailscale / CGNAT
    [InlineData("100.127.255.255", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void PrivateNetwork_ClassifiesAddresses(string address, bool expected)
    {
        Assert.Equal(expected, PrivateNetwork.IsPrivateOrLoopback(IPAddress.Parse(address)));
    }

    [Fact]
    public void PrivateNetwork_NullIsNotPrivate() => Assert.False(PrivateNetwork.IsPrivateOrLoopback(null));
}
