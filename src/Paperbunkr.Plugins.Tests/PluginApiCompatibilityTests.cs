using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Pure-function tests for <c>requiresApi</c> evaluation and failure hints (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §3). Every test injects the host version rather than reading
/// <see cref="PluginApi.Current"/>, so none of them break when the constant is bumped.
/// </summary>
public sealed class PluginApiCompatibilityTests
{
    private static readonly Version Host40 = new(4, 0);
    private static readonly Version Host41 = new(4, 1);

    [Fact]
    public void Absent_attribute_is_compatible_with_no_declaration()
    {
        PluginApiCompatibility result = PluginApiCompatibility.Evaluate(null, Host40);

        Assert.Equal(PluginApiCompatibilityKind.Compatible, result.Kind);
        Assert.Null(result.Declared);
        Assert.Null(result.Reason);
        Assert.False(result.IsBlocked);
    }

    [Theory]
    [InlineData("4.0", 4, 0)]
    [InlineData("4", 4, 0)]
    [InlineData("4.7", 4, 7)]
    [InlineData(" 4.1 ", 4, 1)]
    public void Same_major_is_compatible_whatever_the_minor(string requiresApi, int major, int minor)
    {
        PluginApiCompatibility result = PluginApiCompatibility.Evaluate(requiresApi, Host40);

        Assert.Equal(PluginApiCompatibilityKind.Compatible, result.Kind);
        Assert.Equal(new Version(major, minor), result.Declared);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("5.0")]
    [InlineData("5")]
    [InlineData("3.9")]
    [InlineData("0.1")]
    public void A_different_major_is_blocked_in_either_direction(string requiresApi)
    {
        PluginApiCompatibility result = PluginApiCompatibility.Evaluate(requiresApi, Host40);

        Assert.Equal(PluginApiCompatibilityKind.MajorMismatch, result.Kind);
        Assert.True(result.IsBlocked);
        Assert.Contains("major version mismatch", result.Reason);
        Assert.Contains("this app provides 4.0", result.Reason);
    }

    [Fact]
    public void A_newer_host_major_blocks_an_older_plugin()
    {
        PluginApiCompatibility result = PluginApiCompatibility.Evaluate("4.0", new Version(5, 0));

        Assert.Equal(PluginApiCompatibilityKind.MajorMismatch, result.Kind);
        Assert.Contains("plugin requires API 4.0, this app provides 5.0", result.Reason);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4.1.2")]
    [InlineData("-1")]
    [InlineData("4.x")]
    [InlineData("4.")]
    [InlineData(".1")]
    public void A_malformed_value_is_blocked_with_a_reason(string requiresApi)
    {
        PluginApiCompatibility result = PluginApiCompatibility.Evaluate(requiresApi, Host40);

        Assert.Equal(PluginApiCompatibilityKind.Malformed, result.Kind);
        Assert.True(result.IsBlocked);
        Assert.Null(result.Declared);
        Assert.Contains("invalid requiresApi", result.Reason);
    }

    [Fact]
    public void A_higher_declared_minor_always_gets_a_hint()
    {
        string? hint = PluginApiCompatibility.FailureHint(new Version(4, 2), Host41, driftShapedFailure: false);

        Assert.Equal("plugin declares API 4.2, this app provides 4.1", hint);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void A_declared_minor_at_or_below_the_host_gets_no_hint(int declaredMinor, bool drift)
    {
        Assert.Null(PluginApiCompatibility.FailureHint(new Version(4, declaredMinor), Host41, drift));
    }

    [Fact]
    public void An_absent_declaration_gets_a_hint_only_for_a_drift_shaped_failure()
    {
        Assert.Null(PluginApiCompatibility.FailureHint(null, Host41, driftShapedFailure: false));

        string? hint = PluginApiCompatibility.FailureHint(null, Host41, driftShapedFailure: true);
        Assert.NotNull(hint);
        Assert.Contains("declares no requiresApi", hint);
        Assert.Contains("this app provides 4.1", hint);
    }

    [Fact]
    public void The_original_17_hooks_first_shipped_in_the_4_0_baseline()
    {
        Assert.Equal(new Version(4, 0), PluginHooks.Since(PluginHooks.Startup));

        var original = PluginHooks.ValidHooks.Keys.Except(PluginHooks.DomainEventHooks).ToList();
        Assert.Equal(17, original.Count);
        Assert.All(original, hook => Assert.Equal(new Version(4, 0), PluginHooks.Since(hook)));
    }

    [Fact]
    public void The_four_domain_event_hooks_first_shipped_in_4_1()
    {
        Assert.Equal(
            new[] { PluginHooks.BookRead, PluginHooks.LibraryScanCompleted, PluginHooks.MissingFileDetected, PluginHooks.ReadingListChanged },
            PluginHooks.DomainEventHooks);
        Assert.All(PluginHooks.DomainEventHooks, hook =>
        {
            Assert.Equal(new Version(4, 1), PluginHooks.Since(hook));
            Assert.Contains(hook, PluginHooks.ValidHooks.Keys);
        });
    }

    [Fact]
    public void An_unknown_hook_has_no_since_version()
    {
        Assert.Null(PluginHooks.Since("NotARealHook"));
    }

    [Fact]
    public void The_current_api_is_a_4_x_version()
    {
        Assert.Equal(4, PluginApi.Current.Major);
    }
}
