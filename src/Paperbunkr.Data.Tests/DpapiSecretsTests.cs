using System.Text;
using Paperbunkr.Data.Credentials;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The shared DPAPI wrapper behind both <see cref="CredentialStore"/> and plugin <c>secret</c> settings
/// (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.2). Real DPAPI, so the round-trip tests
/// only run on Windows - which is the only platform the app ships on; elsewhere they return early and the
/// fail-closed tests carry the coverage.
/// </summary>
public class DpapiSecretsTests
{
    private static readonly byte[] EntropyA = Encoding.UTF8.GetBytes("test.entropy.A");
    private static readonly byte[] EntropyB = Encoding.UTF8.GetBytes("test.entropy.B");

    [Fact]
    public void A_value_round_trips_and_is_not_stored_as_plain_text()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string stored = DpapiSecrets.Protect("hunter2", EntropyA);

        Assert.StartsWith(DpapiSecrets.Prefix, stored);
        Assert.DoesNotContain("hunter2", stored);
        Assert.True(DpapiSecrets.IsProtected(stored));
        Assert.Equal("hunter2", DpapiSecrets.TryUnprotect(stored, EntropyA));
    }

    [Fact]
    public void Unicode_and_long_values_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string value = "pässwörd-日本語-🔑-" + new string('x', 5000);

        Assert.Equal(value, DpapiSecrets.TryUnprotect(DpapiSecrets.Protect(value, EntropyA), EntropyA));
    }

    [Fact]
    public void Ciphertext_from_one_stores_entropy_does_not_decrypt_under_another()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string stored = DpapiSecrets.Protect("hunter2", EntropyA);

        Assert.Null(DpapiSecrets.TryUnprotect(stored, EntropyB));
    }

    [Fact]
    public void The_same_value_protects_to_different_ciphertext_each_time()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.NotEqual(DpapiSecrets.Protect("same", EntropyA), DpapiSecrets.Protect("same", EntropyA));
    }

    [Theory]
    [InlineData("plain text, not protected")]
    [InlineData("")]
    public void A_value_that_is_not_in_the_protected_format_never_decrypts(string stored)
    {
        Assert.False(DpapiSecrets.IsProtected(stored));
        Assert.Null(DpapiSecrets.TryUnprotect(stored, EntropyA));
    }

    [Theory]
    [InlineData("dpapi1:!!!not-base64!!!")]
    [InlineData("dpapi1:")]
    [InlineData("dpapi1:AAAA")]
    public void A_corrupt_protected_value_reads_as_unreadable_and_never_throws(string stored)
    {
        Assert.True(DpapiSecrets.IsProtected(stored));
        Assert.Null(DpapiSecrets.TryUnprotect(stored, EntropyA));
    }

    [Fact]
    public void IsSupported_matches_the_platform()
    {
        Assert.Equal(OperatingSystem.IsWindows(), DpapiSecrets.IsSupported);
    }
}
