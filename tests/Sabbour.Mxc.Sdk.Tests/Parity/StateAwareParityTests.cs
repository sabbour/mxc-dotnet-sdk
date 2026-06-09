// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class StateAwareParityTests
{
    private static readonly JsonSerializerOptions s_reflectionJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public void BuildStateAwareEnvelope_ProducesProvisionEnvelopeWithCrossCuttingFieldsLiftedToTopLevel()
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Provision,
            IsolationSessionBackend.WireName,
            containment: IsolationSessionBackend.WireName,
            sandboxId: null,
            new ProvisionEnvelopeCrossCuttingConfig
            {
                Version = "0.6.0-alpha",
                Filesystem = new FilesystemConfig { ReadwritePaths = [@"C:\workspace"] },
                Network = new NetworkConfig { DefaultPolicy = NetworkDefaultPolicy.Block },
                Ui = new UiConfig { Disable = true, Clipboard = ClipboardPolicy.None, Injection = false },
            },
            ReflectionTypeInfo<ProvisionEnvelopeCrossCuttingConfig>());
        using var doc = JsonDocument.Parse(json);
        var env = doc.RootElement;

        Assert.Equal("provision", env.GetProperty("phase").GetString());
        Assert.Equal("isolation_session", env.GetProperty("containment").GetString());
        AssertJsonEquivalent("""{"readwritePaths":["C:\\workspace"]}""", env.GetProperty("filesystem"));
        AssertJsonEquivalent("""{"defaultPolicy":"block"}""", env.GetProperty("network"));
        AssertJsonEquivalent("""{"disable":true,"clipboard":"none","injection":false}""", env.GetProperty("ui"));
        Assert.False(env.TryGetProperty("experimental", out _));
        Assert.False(env.TryGetProperty("sandboxId", out _));
    }

    [Fact]
    public void BuildStateAwareEnvelope_ProducesStartEnvelopeWithConfigurationIdNestedUnderExperimental()
    {
        var env = BuildStartEnvelope(new IsolationSessionStartConfig { ConfigurationId = IsolationSessionConfigurationId.Small }, "iso:reg-abc:prov-123");

        Assert.Equal("start", env.GetProperty("phase").GetString());
        Assert.Equal("iso:reg-abc:prov-123", env.GetProperty("sandboxId").GetString());
        AssertJsonEquivalent(
            """{"isolation_session":{"start":{"configurationId":"small"}}}""",
            env.GetProperty("experimental"));
    }

    [Fact]
    public void BuildStateAwareEnvelope_ProducesExecEnvelopeWithProcessAtTopLevelAndNoExperimentalBlock()
    {
        var env = BuildExecEnvelope(new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "echo hi" } }, "iso:abc");

        Assert.Equal("exec", env.GetProperty("phase").GetString());
        AssertJsonEquivalent("""{"commandLine":"echo hi"}""", env.GetProperty("process"));
        Assert.False(env.TryGetProperty("experimental", out _));
    }

    [Theory]
    [InlineData(Phase.Stop)]
    [InlineData(Phase.Deprovision)]
    public void BuildStateAwareEnvelope_ProducesStopAndDeprovisionEnvelopesCarryingOnlyVersionPhaseAndSandboxId(string phase)
    {
        var json = phase == Phase.Stop
            ? StateAwareEnvelopeBuilder.BuildEnvelope<IsolationSessionStopConfig>(phase, IsolationSessionBackend.WireName, null, "iso:abc", null, MxcJsonContext.Default.IsolationSessionStopConfig)
            : StateAwareEnvelopeBuilder.BuildEnvelope<IsolationSessionDeprovisionConfig>(phase, IsolationSessionBackend.WireName, null, "iso:abc", null, MxcJsonContext.Default.IsolationSessionDeprovisionConfig);
        using var doc = JsonDocument.Parse(json);
        var env = doc.RootElement;

        Assert.Equal(phase, env.GetProperty("phase").GetString());
        Assert.Equal("iso:abc", env.GetProperty("sandboxId").GetString());
        Assert.False(env.TryGetProperty("experimental", out _));
        Assert.True(env.GetProperty("version").GetString() is { Length: > 0 });
    }

    [Fact]
    public void BuildStateAwareEnvelope_UsesCallerSuppliedVersionWhenProvided()
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Provision,
            IsolationSessionBackend.WireName,
            containment: IsolationSessionBackend.WireName,
            sandboxId: null,
            new IsolationSessionProvisionConfig { Version = "0.6.5-alpha" },
            MxcJsonContext.Default.IsolationSessionProvisionConfig);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("0.6.5-alpha", doc.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void BuildStateAwareEnvelope_NestsProvisionUserUnderExperimentalIsolationSessionProvision()
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Provision,
            IsolationSessionBackend.WireName,
            containment: IsolationSessionBackend.WireName,
            sandboxId: null,
            new IsolationSessionProvisionConfig { User = new IsolationSessionUserConfig("alice@contoso.com", "tok") },
            MxcJsonContext.Default.IsolationSessionProvisionConfig);
        using var doc = JsonDocument.Parse(json);

        AssertJsonEquivalent(
            """{"isolation_session":{"provision":{"user":{"upn":"alice@contoso.com","wamToken":"tok"}}}}""",
            doc.RootElement.GetProperty("experimental"));
    }

    [Fact]
    public void BuildStateAwareEnvelope_NestsStartUserUnderExperimentalIsolationSessionStartAlongsideConfigurationId()
    {
        var env = BuildStartEnvelope(
            new IsolationSessionStartConfig
            {
                ConfigurationId = IsolationSessionConfigurationId.Composable,
                User = new IsolationSessionUserConfig("alice@contoso.com", "tok"),
            },
            "iso:alice@contoso.com");

        AssertJsonEquivalent(
            """{"isolation_session":{"start":{"configurationId":"composable","user":{"upn":"alice@contoso.com","wamToken":"tok"}}}}""",
            env.GetProperty("experimental"));
    }

    [Fact]
    public async Task ParseNonExecResponse_UnwrapsResultPayload()
    {
        var client = CreateClient(new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = """{"result":{"sandboxId":"iso:abc"}}""",
            ExitCode = 0,
        }));

        var result = await client.ProvisionSandboxAsync();

        Assert.Equal("iso:abc", result.SandboxId.Value);
    }

    [Theory]
    [InlineData("malformed_request")]
    [InlineData("unsupported_containment")]
    [InlineData("unsupported_phase")]
    [InlineData("backend_unavailable")]
    [InlineData("malformed_id")]
    [InlineData("stale_id")]
    [InlineData("not_provisioned")]
    [InlineData("not_started")]
    [InlineData("already_started")]
    [InlineData("policy_validation")]
    [InlineData("backend_error")]
    public async Task ParseNonExecResponse_ThrowsMxcErrorCarryingEachWireErrorCode(string code)
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = JsonSerializer.Serialize(new { error = new { code, message = "boom" } }),
            ExitCode = 1,
        });
        var client = CreateClient(fake);

        var ex = await Assert.ThrowsAsync<MxcException>(() => client.StartSandboxAsync(new SandboxId<IsolationSessionBackend>("iso:abc")));
        Assert.Equal(code, ex.RawCode);
    }

    [Fact]
    public async Task ParseNonExecResponse_PassesDetailsThroughWhenWireEnvelopeCarriesThem()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = """{"error":{"code":"backend_error","message":"boom","details":{"hresult":"0x80004005"}}}""",
            ExitCode = 1,
        });
        var client = CreateClient(fake);

        var ex = await Assert.ThrowsAsync<MxcException>(() => client.StartSandboxAsync(new SandboxId<IsolationSessionBackend>("iso:abc")));
        Assert.Equal("backend_error", ex.RawCode);
        Assert.Equal("0x80004005", ex.Details!["hresult"]);
    }

    [Fact]
    public async Task ParseNonExecResponse_ThrowsPlainErrorOnUnparseableStdout()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = "not json", ExitCode = 0 });
        var client = CreateClient(fake);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartSandboxAsync(new SandboxId<IsolationSessionBackend>("iso:abc")));
        Assert.IsNotType<MxcException>(ex);
    }

    [Fact]
    public async Task ParseNonExecResponse_ThrowsPlainErrorOnStdoutThatParsesButLacksResultOrError()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = """{"unexpected":"shape"}""", ExitCode = 0 });
        var client = CreateClient(fake);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartSandboxAsync(new SandboxId<IsolationSessionBackend>("iso:abc")));
        Assert.IsNotType<MxcException>(ex);
    }

    [Fact]
    public async Task ProvisionSandbox_BuildsProvisionEnvelopeAndUnwrapsSandboxIdFromResponse()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = """{"result":{"sandboxId":"iso:reg-abc:prov-1","metadata":{"agentUserName":"agent\\u1"}}}""",
            ExitCode = 0,
        });
        var client = CreateClient(fake);

        var result = await client.ProvisionSandboxAsync(
            new IsolationSessionProvisionConfig { Filesystem = new FilesystemConfig { ReadwritePaths = [@"C:\workspace"] } },
            ParityTestHelpers.TestOptions());

        Assert.Equal("iso:reg-abc:prov-1", result.SandboxId.Value);
        Assert.Equal(@"agent\u1", result.Metadata?.AgentUserName);
        Assert.Equal("provision", fake.LastCapture!.Envelope!.Value.GetProperty("phase").GetString());
        Assert.Equal("isolation_session", fake.LastCapture.Envelope.Value.GetProperty("containment").GetString());
        AssertJsonEquivalent("""{"readwritePaths":["C:\\workspace"]}""", fake.LastCapture.Envelope.Value.GetProperty("filesystem"));
        Assert.True(fake.LastCapture.Options!.Experimental);
    }

    [Fact]
    public async Task ProvisionSandbox_ThrowsMxcErrorCarryingBackendUnavailableWhenExecutorReportsIt()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = """{"error":{"code":"backend_unavailable","message":"IsoSessionApp.dll not registered"}}""",
            ExitCode = 1,
        });
        var client = CreateClient(fake);

        var ex = await Assert.ThrowsAsync<MxcException>(() => client.ProvisionSandboxAsync(null, ParityTestHelpers.TestOptions()));
        Assert.Equal("backend_unavailable", ex.RawCode);
    }

    [Fact]
    public async Task ProvisionSandbox_RejectsWhenCancellationTokenFiresBeforeClose()
    {
        using var cts = new CancellationTokenSource();
        var fake = new CancellableStateAwareSpawnRunner();
        var client = CreateClient(fake);

        var promise = client.ProvisionSandboxAsync(null, ParityTestHelpers.TestOptions(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => promise);
        Assert.True(fake.CancellationObserved, "expected spawn runner cancellation to be observed");
    }

    [Fact]
    public async Task StartSandbox_InfersBackendFromSandboxIdPrefixAndNestsConfigurationIdUnderExperimental()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = """{"result":{}}""", ExitCode = 0 });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:reg-abc:prov-1");

        await client.StartSandboxAsync(id, new IsolationSessionStartConfig { ConfigurationId = IsolationSessionConfigurationId.Small }, ParityTestHelpers.TestOptions());

        Assert.Equal("start", fake.LastCapture!.Envelope!.Value.GetProperty("phase").GetString());
        Assert.Equal("iso:reg-abc:prov-1", fake.LastCapture.Envelope.Value.GetProperty("sandboxId").GetString());
        AssertJsonEquivalent(
            """{"isolation_session":{"start":{"configurationId":"small"}}}""",
            fake.LastCapture.Envelope.Value.GetProperty("experimental"));
    }

    [Fact]
    public async Task StopSandbox_BuildsMinimalStopEnvelope()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = """{"result":{}}""", ExitCode = 0 });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:abc");

        await client.StopSandboxAsync(id, null, ParityTestHelpers.TestOptions());

        Assert.Equal("stop", fake.LastCapture!.Envelope!.Value.GetProperty("phase").GetString());
        Assert.Equal("iso:abc", fake.LastCapture.Envelope.Value.GetProperty("sandboxId").GetString());
        Assert.False(fake.LastCapture.Envelope.Value.TryGetProperty("experimental", out _));
    }

    [Fact]
    public void StopSandbox_RejectsWithMalformedIdWhenSandboxIdHasNoRecognisedPrefix()
    {
        var noPrefix = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>("not-a-real-id"));
        Assert.Equal("malformed_id", noPrefix.RawCode);

        var unknownPrefix = Assert.Throws<MxcException>(() => new SandboxId<IsolationSessionBackend>("unknownprefix:abc"));
        Assert.Equal("malformed_id", unknownPrefix.RawCode);
    }

    [Fact]
    public async Task DeprovisionSandbox_BuildsMinimalDeprovisionEnvelope()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = """{"result":{}}""", ExitCode = 0 });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:abc");

        await client.DeprovisionSandboxAsync(id, null, ParityTestHelpers.TestOptions());

        Assert.Equal("deprovision", fake.LastCapture!.Envelope!.Value.GetProperty("phase").GetString());
        Assert.Equal("iso:abc", fake.LastCapture.Envelope.Value.GetProperty("sandboxId").GetString());
    }

    [Fact]
    public async Task ExecInSandboxAsync_ReturnsExecResultOnSuccessfulScriptRun()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = "hello\n", Stderr = "", ExitCode = 0 });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:abc");

        var result = await client.ExecSandboxAsync(id, new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "echo hello" } }, ParityTestHelpers.TestOptions());

        Assert.Equal("hello\n", result.Stdout);
        Assert.Equal("", result.Stderr);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecInSandboxAsync_ReturnsExecResultOnScriptNonZeroExitWhenStdoutIsPlainScriptOutput()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions { Stdout = "oops\n", Stderr = "err\n", ExitCode = 7 });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:abc");

        var result = await client.ExecSandboxAsync(id, new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "fail" } }, ParityTestHelpers.TestOptions());

        Assert.Equal("oops\n", result.Stdout);
        Assert.Equal("err\n", result.Stderr);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task ExecInSandboxAsync_ThrowsTypedMxcErrorOnDispatchFailureWhenStdoutIsCompleteErrorEnvelope()
    {
        var fake = new FakeStateAwareSpawnRunner(new FakeChildOptions
        {
            Stdout = """{"error":{"code":"stale_id","message":"id expired"}}""",
            Stderr = "",
            ExitCode = 1,
        });
        var client = CreateClient(fake);
        var id = new SandboxId<IsolationSessionBackend>("iso:abc");

        var ex = await Assert.ThrowsAsync<MxcException>(() =>
            client.ExecSandboxAsync(id, new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "echo" } }, ParityTestHelpers.TestOptions()));
        Assert.Equal("stale_id", ex.RawCode);
    }

    private static StateAwareSandboxClient<
        IsolationSessionBackend,
        IsolationSessionProvisionConfig,
        IsolationSessionStartConfig,
        IsolationSessionExecConfig,
        IsolationSessionStopConfig,
        IsolationSessionDeprovisionConfig,
        IsolationSessionProvisionMetadata,
        NoMetadata,
        NoMetadata,
        NoMetadata> CreateClient(IStateAwareSpawnRunner runner) => StateAwareSandboxes.CreateIsolationSession(runner);

    private static JsonElement BuildStartEnvelope(IsolationSessionStartConfig config, string sandboxId)
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Start,
            IsolationSessionBackend.WireName,
            containment: null,
            sandboxId,
            config,
            MxcJsonContext.Default.IsolationSessionStartConfig);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildExecEnvelope(IsolationSessionExecConfig config, string sandboxId)
    {
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Exec,
            IsolationSessionBackend.WireName,
            containment: null,
            sandboxId,
            config,
            MxcJsonContext.Default.IsolationSessionExecConfig);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonTypeInfo<T> ReflectionTypeInfo<T>() =>
        (JsonTypeInfo<T>)s_reflectionJson.GetTypeInfo(typeof(T));

    private static void AssertJsonEquivalent(string expectedJson, JsonElement actual)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actualNode = JsonNode.Parse(actual.GetRawText());
        Assert.True(JsonNode.DeepEquals(expected, actualNode), $"Expected {expectedJson}, got {actual.GetRawText()}");
    }

    private sealed record ProvisionEnvelopeCrossCuttingConfig
    {
        [JsonPropertyName("version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Version { get; init; }

        [JsonPropertyName("filesystem")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FilesystemConfig? Filesystem { get; init; }

        [JsonPropertyName("network")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public NetworkConfig? Network { get; init; }

        [JsonPropertyName("ui")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public UiConfig? Ui { get; init; }
    }
}
