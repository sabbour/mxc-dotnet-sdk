// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NSubstitute;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Lifecycle integration tests: provision→start→exec→stop→deprovision with injected spawn fake.
/// </summary>
public class StateAwareLifecycleTests
{
    private readonly IStateAwareSpawnRunner _runner = Substitute.For<IStateAwareSpawnRunner>();

    private StateAwareSandboxClient<
        IsolationSessionBackend,
        IsolationSessionProvisionConfig,
        IsolationSessionStartConfig,
        IsolationSessionExecConfig,
        IsolationSessionStopConfig,
        IsolationSessionDeprovisionConfig,
        IsolationSessionProvisionMetadata,
        NoMetadata,
        NoMetadata,
        NoMetadata> CreateClient() => StateAwareSandboxes.CreateIsolationSession(_runner);

    [Fact]
    public async Task FullLifecycle_ProvisionThroughDeprovision()
    {
        var client = CreateClient();

        // Provision returns sandboxId + metadata
        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{"sandboxId":"iso:test-123","metadata":{"agentUserName":"agent42"}}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var provisionResult = await client.ProvisionSandboxAsync(
            new IsolationSessionProvisionConfig());

        Assert.Equal("iso:test-123", provisionResult.SandboxId.Value);
        Assert.NotNull(provisionResult.Metadata);
        Assert.Equal("agent42", provisionResult.Metadata!.AgentUserName);

        // Start
        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var startResult = await client.StartSandboxAsync(provisionResult.SandboxId,
            new IsolationSessionStartConfig { ConfigurationId = IsolationSessionConfigurationId.Large });
        Assert.NotNull(startResult);

        // Exec (buffered) — success
        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = "hello world\n",
                Stderr = "",
                ExitCode = 0,
            }));

        var execResult = await client.ExecSandboxAsync(provisionResult.SandboxId,
            new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "echo hello world" } });
        Assert.Equal("hello world\n", execResult.Stdout);
        Assert.Equal(0, execResult.ExitCode);

        // Stop
        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var stopResult = await client.StopSandboxAsync(provisionResult.SandboxId);
        Assert.NotNull(stopResult);

        // Deprovision
        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var deprovisionResult = await client.DeprovisionSandboxAsync(provisionResult.SandboxId);
        Assert.NotNull(deprovisionResult);
    }

    [Fact]
    public async Task SandboxId_ThreadedThroughPhases()
    {
        var client = CreateClient();

        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{"sandboxId":"iso:abc"}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var result = await client.ProvisionSandboxAsync();
        var sandboxId = result.SandboxId;

        // Verify the sandboxId appears in subsequent calls
        string? capturedEnvelope = null;
        _runner.SpawnAndCollectAsync(Arg.Do<string>(e => capturedEnvelope = e), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        await client.StartSandboxAsync(sandboxId);

        Assert.NotNull(capturedEnvelope);
        Assert.Contains("\"sandboxId\":\"iso:abc\"", capturedEnvelope);
    }

    [Fact]
    public async Task ExecBuffered_ErrorEnvelope_ThrowsMxcException_KnownCode()
    {
        var client = CreateClient();

        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"error":{"code":"backend_error","message":"sandbox crashed"}}""",
                Stderr = "",
                ExitCode = 1,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:err1");
        var ex = await Assert.ThrowsAsync<MxcException>(() => client.ExecSandboxAsync(
            sandboxId,
            new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "fail" } }));

        Assert.Equal(ErrorCode.BackendError, ex.Code);
        Assert.Equal("backend_error", ex.RawCode);
        Assert.Equal("sandbox crashed", ex.Message);
    }

    [Fact]
    public async Task ExecBuffered_ErrorEnvelope_ThrowsMxcException_UnknownCode()
    {
        var client = CreateClient();

        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"error":{"code":"future_error_code","message":"something new","details":{"extra":"data"}}}""",
                Stderr = "",
                ExitCode = 1,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:err2");
        var ex = await Assert.ThrowsAsync<MxcException>(() => client.ExecSandboxAsync(
            sandboxId,
            new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "fail" } }));

        Assert.Null(ex.Code); // unknown code
        Assert.Equal("future_error_code", ex.RawCode);
        Assert.Equal("something new", ex.Message);
        Assert.NotNull(ex.Details);
        Assert.Equal("data", ex.Details!["extra"]);
    }

    [Fact]
    public async Task ExecBuffered_NonZeroExit_NoErrorEnvelope_ReturnsResult()
    {
        var client = CreateClient();

        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = "script output",
                Stderr = "warning",
                ExitCode = 42,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:nr1");
        var result = await client.ExecSandboxAsync(
            sandboxId,
            new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "exit 42" } });

        Assert.Equal(42, result.ExitCode);
        Assert.Equal("script output", result.Stdout);
        Assert.Equal("warning", result.Stderr);
    }

    [Fact]
    public void ExecStreaming_ReturnsIPtyConnection()
    {
        var client = CreateClient();
        var fakePty = Substitute.For<IPtyConnection>();

        _runner.SpawnStreaming(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>())
            .Returns(fakePty);

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:stream1");
        var connection = client.ExecSandbox(
            sandboxId,
            new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "bash" } });

        Assert.Same(fakePty, connection);
    }

    [Fact]
    public async Task NonExecPhase_ErrorEnvelope_ThrowsMxcException()
    {
        var client = CreateClient();

        _runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"error":{"code":"backend_unavailable","message":"no backend"}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:ne1");
        var ex = await Assert.ThrowsAsync<MxcException>(() =>
            client.StartSandboxAsync(sandboxId));

        Assert.Equal("backend_unavailable", ex.RawCode);
    }

    [Fact]
    public async Task Provision_EnvelopeContainsContainment()
    {
        var client = CreateClient();
        string? capturedEnvelope = null;

        _runner.SpawnAndCollectAsync(Arg.Do<string>(e => capturedEnvelope = e), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """{"result":{"sandboxId":"iso:prov1"}}""",
                Stderr = "",
                ExitCode = 0,
            }));

        await client.ProvisionSandboxAsync();

        Assert.NotNull(capturedEnvelope);
        Assert.Contains("\"containment\":\"isolation_session\"", capturedEnvelope);
        Assert.Contains("\"phase\":\"provision\"", capturedEnvelope);
        Assert.DoesNotContain("\"sandboxId\"", capturedEnvelope);
    }
}
