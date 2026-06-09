// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Sabbour.Mxc.Sdk.Internal;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Unit tests for ported helper.ts functions: VersionHelper and NetworkPolicyHelper.
/// </summary>
public class HelperTests
{
    // -----------------------------------------------------------------------
    // VersionHelper.ValidatePolicyVersion
    // -----------------------------------------------------------------------

    [Fact]
    public void VersionHelper_ThrowsOnNull()
    {
        Assert.Throws<ArgumentException>(() => VersionHelper.ValidatePolicyVersion(null!));
    }

    [Fact]
    public void VersionHelper_ThrowsOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => VersionHelper.ValidatePolicyVersion(""));
    }

    [Fact]
    public void VersionHelper_ThrowsOnNonSemver()
    {
        var ex = Assert.Throws<ArgumentException>(() => VersionHelper.ValidatePolicyVersion("abc"));
        Assert.Contains("must be valid semver", ex.Message);
    }

    [Theory]
    [InlineData("0.3.0")]
    [InlineData("0.2.5-alpha")]
    [InlineData("0.1.0")]
    public void VersionHelper_ThrowsOnTooOld(string version)
    {
        var ex = Assert.Throws<ArgumentException>(() => VersionHelper.ValidatePolicyVersion(version));
        Assert.Contains("older than supported", ex.Message);
        Assert.Contains("min: 0.4.x", ex.Message);
    }

    [Theory]
    [InlineData("0.8.0")]
    [InlineData("1.0.0")]
    [InlineData("2.0.0-alpha")]
    public void VersionHelper_ThrowsOnTooNew(string version)
    {
        var ex = Assert.Throws<ArgumentException>(() => VersionHelper.ValidatePolicyVersion(version));
        Assert.Contains("newer than supported", ex.Message);
        Assert.Contains("max: 0.7.x", ex.Message);
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.4.0-alpha")]
    [InlineData("0.4.9")]
    [InlineData("0.5.0")]
    [InlineData("0.5.0-alpha")]
    [InlineData("0.6.0")]
    [InlineData("0.6.1")]
    [InlineData("0.7.0")]
    [InlineData("0.7.0-alpha")]
    [InlineData("0.7.99")]
    public void VersionHelper_AcceptsValidRange(string version)
    {
        // Should not throw
        VersionHelper.ValidatePolicyVersion(version);
    }

    [Fact]
    public void VersionHelper_Constants_MatchTsSource()
    {
        Assert.Equal("0.7.0-alpha", VersionHelper.SupportedVersion);
        Assert.Equal("0.4.0-alpha", VersionHelper.MinVersion);
    }

    // -----------------------------------------------------------------------
    // NetworkPolicyHelper.ApplyLinuxNetworkPolicy
    // -----------------------------------------------------------------------

    [Fact]
    public void NetworkPolicy_ReturnsNull_WhenNull()
    {
        Assert.Null(NetworkPolicyHelper.ApplyLinuxNetworkPolicy(null));
    }

    [Fact]
    public void NetworkPolicy_NoChange_WhenNoHostRules()
    {
        var network = new NetworkConfig { DefaultPolicy = NetworkDefaultPolicy.Allow };
        var result = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(network);
        Assert.Null(result!.EnforcementMode);
    }

    [Fact]
    public void NetworkPolicy_PromotesToFirewall_WhenHostRulesNoProxy()
    {
        var network = new NetworkConfig
        {
            DefaultPolicy = NetworkDefaultPolicy.Allow,
            AllowedHosts = ["api.example.com"],
        };

        var result = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(network);
        Assert.Equal(NetworkEnforcementMode.Firewall, result!.EnforcementMode);
    }

    [Fact]
    public void NetworkPolicy_DoesNotPromote_WhenProxyPresent()
    {
        var network = new NetworkConfig
        {
            DefaultPolicy = NetworkDefaultPolicy.Allow,
            AllowedHosts = ["api.example.com"],
            Proxy = ProxyConfig.Localhost(8080),
        };

        var result = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(network);
        Assert.Null(result!.EnforcementMode);
    }

    [Fact]
    public void NetworkPolicy_PromotesToFirewall_WithBlockedHosts()
    {
        var network = new NetworkConfig
        {
            DefaultPolicy = NetworkDefaultPolicy.Block,
            BlockedHosts = ["evil.com"],
        };

        var result = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(network);
        Assert.Equal(NetworkEnforcementMode.Firewall, result!.EnforcementMode);
    }
}
