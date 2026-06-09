// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.StateAware;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Tests for state-aware type system: SandboxId branding, ConfigurationId wire values,
/// IsolationSessionUserConfig redaction.
/// </summary>
public class StateAwareTypeTests
{
    [Fact]
    public void SandboxId_ValidPrefix_Succeeds()
    {
        var id = new SandboxId<IsolationSessionBackend>("iso:abc123");
        Assert.Equal("iso:abc123", id.Value);
        Assert.Equal("iso:abc123", id.ToString());
    }

    [Fact]
    public void SandboxId_NullValue_Throws_MxcException()
    {
        var ex = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>(null!));
        Assert.Equal("malformed_id", ex.RawCode);
    }

    [Fact]
    public void SandboxId_EmptyValue_Throws_MxcException()
    {
        var ex = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>(""));
        Assert.Equal("malformed_id", ex.RawCode);
    }

    [Fact]
    public void SandboxId_NoColon_Throws_MxcException()
    {
        var ex = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>("noprefix"));
        Assert.Equal("malformed_id", ex.RawCode);
        Assert.Contains("must carry a backend prefix", ex.Message);
    }

    [Fact]
    public void SandboxId_WrongPrefix_Throws_MxcException()
    {
        var ex = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>("wrong:abc123"));
        Assert.Equal("malformed_id", ex.RawCode);
        Assert.Contains("does not match expected 'iso'", ex.Message);
    }

    // Cross-backend mixing is a compile-time error. This commented example would NOT compile:
    // void WouldNotCompile(SandboxId<IsolationSessionBackend> id)
    // {
    //     // Cannot pass SandboxId<IsolationSessionBackend> where SandboxId<SomeOtherBackend> is expected.
    //     // SandboxId<SomeOtherBackend> other = id; // CS0029
    // }

    [Theory]
    [InlineData(IsolationSessionConfigurationId.Small, "small")]
    [InlineData(IsolationSessionConfigurationId.Medium, "medium")]
    [InlineData(IsolationSessionConfigurationId.Large, "large")]
    [InlineData(IsolationSessionConfigurationId.Composable, "composable")]
    public void ConfigurationId_WireValues_MatchExpected(IsolationSessionConfigurationId value, string expected)
    {
        var json = JsonSerializer.Serialize(value);
        Assert.Equal($"\"{expected}\"", json);

        var deserialized = JsonSerializer.Deserialize<IsolationSessionConfigurationId>($"\"{expected}\"");
        Assert.Equal(value, deserialized);
    }

    [Fact]
    public void IsolationSessionUserConfig_ToString_RedactsWamToken()
    {
        var config = new IsolationSessionUserConfig("user@example.com", "secret-token-123");
        var str = config.ToString();
        Assert.Contains("user@example.com", str);
        Assert.Contains("<redacted>", str);
        Assert.DoesNotContain("secret-token-123", str);
    }

    [Fact]
    public void IsolationSessionUserConfig_Json_EmitsWamToken()
    {
        var config = new IsolationSessionUserConfig("user@example.com", "secret-token-123");
        var json = JsonSerializer.Serialize(config, MxcJsonContext.Default.IsolationSessionUserConfig);
        Assert.Contains("\"wamToken\":\"secret-token-123\"", json);
        Assert.Contains("\"upn\":\"user@example.com\"", json);
    }

    [Fact]
    public void IsolationSessionBackend_WireNameAndPrefix()
    {
        Assert.Equal("isolation_session", IsolationSessionBackend.WireName);
        Assert.Equal("iso", IsolationSessionBackend.SandboxIdPrefix);
    }
}
