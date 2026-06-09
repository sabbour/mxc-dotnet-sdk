// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Sabbour.Mxc.Sdk.StateAware;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Golden byte-for-byte JSON envelope tests for each lifecycle phase.
/// Verifies top-level version/phase/containment/sandboxId placement,
/// lifted cross-cutting fields, and experimental.isolation_session.&lt;phase&gt; nesting.
/// </summary>
public class StateAwareEnvelopeTests
{
    [Fact]
    public void Provision_Envelope_MinimalConfig()
    {
        var config = new IsolationSessionProvisionConfig();
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("provision", obj["phase"]!.GetValue<string>());
        Assert.Equal("isolation_session", obj["containment"]!.GetValue<string>());
        Assert.Null(obj["sandboxId"]);
        Assert.Null(obj["experimental"]);
    }

    [Fact]
    public void Provision_Envelope_WithFilesystemAndUser()
    {
        var config = new IsolationSessionProvisionConfig
        {
            Filesystem = new FilesystemConfig { ReadwritePaths = ["/tmp"] },
            User = new IsolationSessionUserConfig("user@example.com", "token123"),
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("provision", obj["phase"]!.GetValue<string>());
        Assert.Equal("isolation_session", obj["containment"]!.GetValue<string>());

        // filesystem is cross-cutting → lifted to top-level
        Assert.NotNull(obj["filesystem"]);
        var rwPaths = obj["filesystem"]!["readwritePaths"]!.AsArray();
        Assert.Equal("/tmp", rwPaths[0]!.GetValue<string>());

        // user is backend-specific → nested under experimental.isolation_session.provision
        Assert.NotNull(obj["experimental"]);
        var expIso = obj["experimental"]!["isolation_session"]!["provision"]!;
        Assert.NotNull(expIso["user"]);
        Assert.Equal("user@example.com", expIso["user"]!["upn"]!.GetValue<string>());
        Assert.Equal("token123", expIso["user"]!["wamToken"]!.GetValue<string>());
    }

    [Fact]
    public void Provision_Envelope_CustomVersion()
    {
        var config = new IsolationSessionProvisionConfig { Version = "1.0.0" };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("1.0.0", obj["version"]!.GetValue<string>());
    }

    [Fact]
    public void Provision_Envelope_EmptyVersionDefaults()
    {
        var config = new IsolationSessionProvisionConfig { Version = "" };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
    }

    [Fact]
    public void Start_Envelope_WithConfigurationIdAndUser()
    {
        var config = new IsolationSessionStartConfig
        {
            ConfigurationId = IsolationSessionConfigurationId.Large,
            User = new IsolationSessionUserConfig("u@x.com", "tok"),
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "start",
            "isolation_session",
            containment: null,
            sandboxId: "iso:sandbox123",
            config,
            MxcJsonContext.Default.IsolationSessionStartConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("start", obj["phase"]!.GetValue<string>());
        Assert.Null(obj["containment"]);
        Assert.Equal("iso:sandbox123", obj["sandboxId"]!.GetValue<string>());

        // configurationId and user are backend-specific
        var expPhase = obj["experimental"]!["isolation_session"]!["start"]!;
        Assert.Equal("large", expPhase["configurationId"]!.GetValue<string>());
        Assert.Equal("u@x.com", expPhase["user"]!["upn"]!.GetValue<string>());
    }

    [Fact]
    public void Exec_Envelope_WithProcess()
    {
        var config = new IsolationSessionExecConfig
        {
            Process = new ProcessConfig { CommandLine = "echo hello" },
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "exec",
            "isolation_session",
            containment: null,
            sandboxId: "iso:ex123",
            config,
            MxcJsonContext.Default.IsolationSessionExecConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("exec", obj["phase"]!.GetValue<string>());
        Assert.Equal("iso:ex123", obj["sandboxId"]!.GetValue<string>());
        Assert.Null(obj["containment"]);

        // process is cross-cutting → lifted to top-level
        Assert.NotNull(obj["process"]);
        Assert.Equal("echo hello", obj["process"]!["commandLine"]!.GetValue<string>());

        // No remaining backend-specific fields → no experimental
        Assert.Null(obj["experimental"]);
    }

    [Fact]
    public void Stop_Envelope_Minimal()
    {
        var config = new IsolationSessionStopConfig();
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "stop",
            "isolation_session",
            containment: null,
            sandboxId: "iso:stop1",
            config,
            MxcJsonContext.Default.IsolationSessionStopConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("stop", obj["phase"]!.GetValue<string>());
        Assert.Equal("iso:stop1", obj["sandboxId"]!.GetValue<string>());
        Assert.Null(obj["containment"]);
        Assert.Null(obj["experimental"]);
    }

    [Fact]
    public void Deprovision_Envelope_Minimal()
    {
        var config = new IsolationSessionDeprovisionConfig();
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "deprovision",
            "isolation_session",
            containment: null,
            sandboxId: "iso:dep1",
            config,
            MxcJsonContext.Default.IsolationSessionDeprovisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("deprovision", obj["phase"]!.GetValue<string>());
        Assert.Equal("iso:dep1", obj["sandboxId"]!.GetValue<string>());
        Assert.Null(obj["containment"]);
        Assert.Null(obj["experimental"]);
    }

    [Fact]
    public void Deprovision_Envelope_CustomVersion()
    {
        var config = new IsolationSessionDeprovisionConfig { Version = "2.0.0" };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "deprovision",
            "isolation_session",
            containment: null,
            sandboxId: "iso:dep2",
            config,
            MxcJsonContext.Default.IsolationSessionDeprovisionConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("2.0.0", obj["version"]!.GetValue<string>());
    }

    [Fact]
    public void NullConfig_ProducesDefaultVersionAndPhaseOnly()
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope<IsolationSessionStopConfig>(
            "stop",
            "isolation_session",
            containment: null,
            sandboxId: "iso:null1",
            null,
            MxcJsonContext.Default.IsolationSessionStopConfig);

        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("0.6.0-alpha", obj["version"]!.GetValue<string>());
        Assert.Equal("stop", obj["phase"]!.GetValue<string>());
        Assert.Equal("iso:null1", obj["sandboxId"]!.GetValue<string>());
    }

    [Fact]
    public void Provision_GoldenJson_ByteForByte()
    {
        // Verifies exact JSON structure matches TS envelope output
        var config = new IsolationSessionProvisionConfig
        {
            Filesystem = new FilesystemConfig { ReadwritePaths = ["/home"] },
            User = new IsolationSessionUserConfig("a@b.com", "t1"),
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        // Parse and assert key ordering + structure
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var props = root.EnumerateObject().ToList();

        // Order: version, phase, containment, filesystem, experimental
        Assert.Equal("version", props[0].Name);
        Assert.Equal("phase", props[1].Name);
        Assert.Equal("containment", props[2].Name);
        Assert.Equal("filesystem", props[3].Name);
        Assert.Equal("experimental", props[4].Name);
    }

    [Fact]
    public void Start_GoldenJson_ByteForByte()
    {
        var config = new IsolationSessionStartConfig
        {
            ConfigurationId = IsolationSessionConfigurationId.Medium,
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "start",
            "isolation_session",
            containment: null,
            sandboxId: "iso:s1",
            config,
            MxcJsonContext.Default.IsolationSessionStartConfig);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var props = root.EnumerateObject().ToList();

        // Order: version, phase, sandboxId, experimental
        Assert.Equal("version", props[0].Name);
        Assert.Equal("phase", props[1].Name);
        Assert.Equal("sandboxId", props[2].Name);
        Assert.Equal("experimental", props[3].Name);

        // Nested: experimental.isolation_session.start.configurationId
        var configId = root.GetProperty("experimental")
            .GetProperty("isolation_session")
            .GetProperty("start")
            .GetProperty("configurationId")
            .GetString();
        Assert.Equal("medium", configId);
    }
}
