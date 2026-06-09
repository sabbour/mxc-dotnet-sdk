// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Sandbox;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

#region Fakes

/// <summary>
/// Fake IPtyConnection for unit tests. Records all operations.
/// </summary>
internal sealed class FakePtyConnection : IPtyConnection
{
    private readonly TaskCompletionSource<PtyExitEvent> _exitTcs = new();
    private readonly List<string> _writtenStrings = new();
    private readonly List<(int Cols, int Rows)> _resizes = new();

    public int ProcessId => 12345;
    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<PtyExitEvent>? Exited;

    public IReadOnlyList<string> WrittenStrings => _writtenStrings;
    public IReadOnlyList<(int Cols, int Rows)> Resizes => _resizes;
    public bool Killed { get; private set; }

    public void Write(string data) => _writtenStrings.Add(data);
    public void Write(ReadOnlySpan<byte> data) => _writtenStrings.Add(Encoding.UTF8.GetString(data));
    public void Resize(int columns, int rows) => _resizes.Add((columns, rows));
    public void Kill() => Killed = true;

    public Task<PtyExitEvent> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken == default) return _exitTcs.Task;
        // Fix #9: use WaitAsync to cancel the caller, not the shared TCS
        return _exitTcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Simulate data arriving from the PTY.</summary>
    public void SimulateData(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        DataReceived?.Invoke(new ReadOnlyMemory<byte>(bytes));
    }

    /// <summary>Simulate process exit.</summary>
    public void SimulateExit(int exitCode, int? signal = null)
    {
        var evt = new PtyExitEvent(exitCode, signal);
        _exitTcs.TrySetResult(evt);
        Exited?.Invoke(evt);
    }

    public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }
    public void Dispose() { }
}

/// <summary>
/// Fake PTY factory that returns a controllable FakePtyConnection.
/// </summary>
internal sealed class FakePtyConnectionFactory : IPtyConnectionFactory
{
    public FakePtyConnection? LastConnection { get; private set; }
    public string? LastExecutablePath { get; private set; }
    public IReadOnlyList<string>? LastArgs { get; private set; }

    private readonly Func<FakePtyConnection>? _connectionProvider;

    public FakePtyConnectionFactory(Func<FakePtyConnection>? connectionProvider = null)
    {
        _connectionProvider = connectionProvider;
    }

    public Task<IPtyConnection> SpawnAsync(
        string executablePath,
        IReadOnlyList<string> args,
        PtyOptions? options,
        CancellationToken cancellationToken = default)
    {
        LastExecutablePath = executablePath;
        LastArgs = args;
        var conn = _connectionProvider?.Invoke() ?? new FakePtyConnection();
        LastConnection = conn;
        return Task.FromResult<IPtyConnection>(conn);
    }
}

/// <summary>
/// Fake process factory for testing pipe-mode spawn.
/// </summary>
internal sealed class FakeProcessConnectionFactory : IProcessConnectionFactory
{
    public string? LastExecutablePath { get; private set; }
    public IReadOnlyList<string>? LastArgs { get; private set; }

    /// <summary>Optional override: cmd.exe arguments to execute instead of default "echo test".</summary>
    private readonly string? _cmdArgs;

    public FakeProcessConnectionFactory(string? cmdArgs = null)
    {
        _cmdArgs = cmdArgs;
    }

    public ProcessConnection Spawn(string executablePath, IReadOnlyList<string> args, string? workingDirectory)
    {
        LastExecutablePath = executablePath;
        LastArgs = args;
        var shellArgs = _cmdArgs ?? "echo test";
        return ProcessConnection.Spawn("cmd.exe", ["/c", shellArgs], workingDirectory);
    }
}

#endregion

public class SandboxSpawnTests
{
    [Fact]
    public void SpawnHelper_BuildsConfigBase64Argv()
    {
        var config = TestConfigHelper.CreateTestConfig("echo hello");
        var options = new SandboxSpawnOptions
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            SkipPlatformCheck = true,
        };

        var result = SpawnHelper.PrepareSpawn(config, options);

        Assert.Equal(@"C:\Windows\System32\cmd.exe", result.ExecutablePath);
        Assert.Contains("--config-base64", result.Args);

        // Verify base64 decodes to valid JSON containing commandLine
        var base64Idx = result.Args.ToList().IndexOf("--config-base64");
        var base64Value = result.Args[base64Idx + 1];
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Value));
        Assert.Contains("echo hello", json);
    }

    [Fact]
    public void SpawnHelper_IncludesDryRunFlag()
    {
        var config = TestConfigHelper.CreateTestConfig("echo test");
        var options = new SandboxSpawnOptions
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            DryRun = true,
        };

        var result = SpawnHelper.PrepareSpawn(config, options);
        Assert.Contains("--dry-run", result.Args);
    }

    [Fact]
    public void SpawnHelper_IncludesDebugFlag()
    {
        var config = TestConfigHelper.CreateTestConfig("echo test");
        var options = new SandboxSpawnOptions
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Debug = true,
            LogDir = @"C:\logs",
        };

        var result = SpawnHelper.PrepareSpawn(config, options);
        Assert.Contains("--debug", result.Args);
        Assert.Contains("--log-file", result.Args);
    }

    [Fact]
    public void SpawnHelper_IncludesExperimentalFlag()
    {
        var config = TestConfigHelper.CreateTestConfig("echo test");
        var options = new SandboxSpawnOptions
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Experimental = true,
        };

        var result = SpawnHelper.PrepareSpawn(config, options);
        Assert.Contains("--experimental", result.Args);
    }

    [Fact]
    public void SpawnHelper_ThrowsWhenNoCommandLine()
    {
        var config = new ContainerConfig
        {
            Version = "0.5.0",
            Process = new ProcessConfig { CommandLine = "" },
        };

        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };
        Assert.Throws<InvalidOperationException>(() => SpawnHelper.PrepareSpawn(config, options));
    }

    [Fact]
    public void SpawnHelper_ThrowsWhenExecutableNotFound()
    {
        var config = TestConfigHelper.CreateTestConfig("echo test");
        var options = new SandboxSpawnOptions
        {
            ExecutablePath = @"C:\nonexistent\wxc-exec.exe",
        };

        Assert.Throws<FileNotFoundException>(() => SpawnHelper.PrepareSpawn(config, options));
    }

    [Fact]
    public void SpawnHelper_LargeConfig_ProducesValidBase64()
    {
        // Test with a realistically large config (long paths, many env vars)
        var envVars = Enumerable.Range(0, 100)
            .Select(i => $"VARIABLE_{i}={new string('x', 200)}")
            .ToList();

        var config = new ContainerConfig
        {
            Version = "0.5.0",
            Process = new ProcessConfig
            {
                CommandLine = "python -c 'print(\"hello world\")'",
                Env = envVars,
            },
            Filesystem = new FilesystemConfig
            {
                ReadwritePaths = Enumerable.Range(0, 50).Select(i => $"/workspace/dir{i}/{new string('a', 100)}").ToList(),
            },
        };

        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };
        var result = SpawnHelper.PrepareSpawn(config, options);

        var base64Idx = result.Args.ToList().IndexOf("--config-base64");
        var base64Value = result.Args[base64Idx + 1];

        // Verify it's valid base64 and decodes to valid JSON
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Value));
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
        // Verify it's sizable (proves large configs work)
        Assert.True(base64Value.Length > 10000, $"Expected large base64, got {base64Value.Length} chars");
    }
}

public class ErrorEnvelopeTests
{
    // --- TryParseErrorEnvelope (state-aware path): parses stdout.Trim() as one JSON object ---

    [Fact]
    public void TryParseErrorEnvelope_ParsesWholeStdoutAsEnvelope()
    {
        var output = """{"error":{"code":"backend_error","message":"something failed"}}""";
        var result = SpawnHelper.TryParseErrorEnvelope(output);

        Assert.NotNull(result);
        Assert.Equal(ErrorCode.BackendError, result.Code);
        Assert.Equal("something failed", result.Message);
    }

    [Fact]
    public void TryParseErrorEnvelope_FindsUnknownCode()
    {
        var output = "{\"error\":{\"code\":\"unknown_future_code\",\"message\":\"new error\"}}";
        var result = SpawnHelper.TryParseErrorEnvelope(output);

        Assert.NotNull(result);
        Assert.Null(result.Code); // Unknown codes → null Code
        Assert.Equal("unknown_future_code", result.RawCode);
        Assert.Equal("new error", result.Message);
    }

    [Fact]
    public void TryParseErrorEnvelope_WithDetails()
    {
        var output = "{\"error\":{\"code\":\"malformed_request\",\"message\":\"bad\",\"details\":{\"field\":\"version\",\"count\":42}}}";
        var result = SpawnHelper.TryParseErrorEnvelope(output);

        Assert.NotNull(result);
        Assert.NotNull(result.Details);
        Assert.Equal("version", result.Details["field"]);
    }

    [Fact]
    public void TryParseErrorEnvelope_ReturnsNullForNonJsonOutput()
    {
        var output = "normal output\nno json here\njust text";
        Assert.Null(SpawnHelper.TryParseErrorEnvelope(output));
    }

    [Fact]
    public void TryParseErrorEnvelope_ReturnsNullForMultiLineWithErrorLine()
    {
        // B1 fix: a user script that prints {"error":...} on a line among other output
        // is NOT treated as an MXC dispatch failure.
        var output = "some random line\n{\"error\":{\"code\":\"backend_error\",\"message\":\"something failed\"}}\nmore text";
        Assert.Null(SpawnHelper.TryParseErrorEnvelope(output));
    }

    [Fact]
    public void TryParseErrorEnvelope_ReturnsNullWhenErrorPropertyIsNotAnObject()
    {
        var output = """{"error":"just a string"}""";
        Assert.Null(SpawnHelper.TryParseErrorEnvelope(output));
    }

    [Fact]
    public void TryParseErrorEnvelope_ReturnsNullWhenNoCodeProperty()
    {
        var output = """{"error":{"message":"no code"}}""";
        Assert.Null(SpawnHelper.TryParseErrorEnvelope(output));
    }

    [Fact]
    public void TryParseErrorEnvelope_TrimsWhitespace()
    {
        var output = "  \n{\"error\":{\"code\":\"backend_error\",\"message\":\"trimmed\"}} \n ";
        var result = SpawnHelper.TryParseErrorEnvelope(output);
        Assert.NotNull(result);
        Assert.Equal("trimmed", result.Message);
    }

    // --- TryParseErrorEnvelopeFromLines (one-shot PTY path): line-scanning ---

    [Fact]
    public void TryParseErrorEnvelopeFromLines_FindsKnownCode()
    {
        var output = "some random line\n{\"error\":{\"code\":\"backend_error\",\"message\":\"something failed\"}}\nmore text";
        var result = SpawnHelper.TryParseErrorEnvelopeFromLines(output);

        Assert.NotNull(result);
        Assert.Equal(ErrorCode.BackendError, result.Code);
        Assert.Equal("something failed", result.Message);
    }

    [Fact]
    public void TryParseErrorEnvelopeFromLines_SkipsInvalidJson()
    {
        var output = "{not valid json}\n{\"error\":{\"code\":\"backend_error\",\"message\":\"found it\"}}";
        var result = SpawnHelper.TryParseErrorEnvelopeFromLines(output);
        Assert.NotNull(result);
        Assert.Equal("found it", result.Message);
    }

    [Fact]
    public void TryParseErrorEnvelopeFromLines_ReturnsNullForNoEnvelope()
    {
        var output = "normal output\nno json here\njust text";
        Assert.Null(SpawnHelper.TryParseErrorEnvelopeFromLines(output));
    }
}

public class SandboxSpawnerAsyncTests
{
    [Fact]
    public async Task SpawnSandboxAsync_BuffersCombinedOutput()
    {
        var fakePty = new FakePtyConnection();
        var factory = new FakePtyConnectionFactory(() => fakePty);
        var spawner = new SandboxSpawner(factory, new FakeProcessConnectionFactory());

        var config = TestConfigHelper.CreateTestConfig("echo hello");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        var task = spawner.SpawnSandboxAsync(config, options);

        // Simulate output then exit
        fakePty.SimulateData("hello world\r\n");
        fakePty.SimulateData("more output\r\n");
        fakePty.SimulateExit(0);

        var result = await task;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello world", result.Stdout);
        Assert.Contains("more output", result.Stdout);
        Assert.Equal("", result.Stderr); // PTY mode: stderr always empty
    }

    [Fact]
    public async Task SpawnSandboxAsync_ThrowsMxcExceptionOnErrorEnvelope()
    {
        var fakePty = new FakePtyConnection();
        var factory = new FakePtyConnectionFactory(() => fakePty);
        var spawner = new SandboxSpawner(factory, new FakeProcessConnectionFactory());

        var config = TestConfigHelper.CreateTestConfig("bad command");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        var task = spawner.SpawnSandboxAsync(config, options);

        fakePty.SimulateData("{\"error\":{\"code\":\"backend_error\",\"message\":\"container failed\"}}\n");
        fakePty.SimulateExit(1);

        var ex = await Assert.ThrowsAsync<MxcException>(() => task);
        Assert.Equal(ErrorCode.BackendError, ex.Code);
        Assert.Equal("container failed", ex.Message);
    }

    [Fact]
    public async Task SpawnSandboxAsync_CancellationThrowsOperationCanceled()
    {
        var fakePty = new FakePtyConnection();
        var factory = new FakePtyConnectionFactory(() => fakePty);
        var spawner = new SandboxSpawner(factory, new FakeProcessConnectionFactory());

        var config = TestConfigHelper.CreateTestConfig("long running");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        using var cts = new CancellationTokenSource();
        var task = spawner.SpawnSandboxAsync(config, options, cts.Token);

        // Cancel before exit
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SpawnSandboxAsync_NonzeroExitWithoutEnvelope_ReturnsResult()
    {
        var fakePty = new FakePtyConnection();
        var factory = new FakePtyConnectionFactory(() => fakePty);
        var spawner = new SandboxSpawner(factory, new FakeProcessConnectionFactory());

        var config = TestConfigHelper.CreateTestConfig("fail");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        var task = spawner.SpawnSandboxAsync(config, options);

        fakePty.SimulateData("some error text\n");
        fakePty.SimulateExit(42);

        var result = await task;
        Assert.Equal(42, result.ExitCode);
        Assert.Contains("some error text", result.Stdout);
    }
}

public class SandboxPipeModeTests
{
    [Fact]
    public async Task PipeMode_NonzeroExit_WithErrorEnvelopeInStdout_DoesNotThrow()
    {
        // TS pipe-mode (usePty:false) returns the raw child without error-envelope parsing.
        // A user script that prints {"error":...} and exits nonzero must NOT be misclassified.
        var errorJson = "{\"error\":{\"code\":\"backend_error\",\"message\":\"user script output\"}}";
        var cmdArgs = $"echo {errorJson}& exit /b 1";
        var processFactory = new FakeProcessConnectionFactory(cmdArgs);
        var ptyFactory = new FakePtyConnectionFactory();
        var spawner = new SandboxSpawner(ptyFactory, processFactory);

        var config = TestConfigHelper.CreateTestConfig("user-script");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        var result = await spawner.SpawnSandboxProcessAsync(config, options);

        // Must return result with nonzero exit, NOT throw MxcException
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error", result.Stdout);
        Assert.Contains("backend_error", result.Stdout);
    }

    [Fact]
    public async Task PipeMode_ZeroExit_ReturnsOutput()
    {
        var processFactory = new FakeProcessConnectionFactory("echo success");
        var ptyFactory = new FakePtyConnectionFactory();
        var spawner = new SandboxSpawner(ptyFactory, processFactory);

        var config = TestConfigHelper.CreateTestConfig("echo ok");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };

        var result = await spawner.SpawnSandboxProcessAsync(config, options);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("success", result.Stdout);
    }
}

public class SandboxFactoryTests
{
    [Fact]
    public void CreateConfigFromPolicy_SeatbeltRejection()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };

        var ex = Assert.Throws<NotSupportedException>(
            () => SandboxFactory.CreateConfigFromPolicy(policy, "seatbelt"));

        Assert.Contains("Containment type 'seatbelt' is not yet supported", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_DefaultProcess_SetsContainment()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = SandboxFactory.CreateConfigFromPolicy(policy);

        Assert.NotNull(config.Containment);
        Assert.Equal("process", config.Containment.Value.ToString());
    }

    [Fact]
    public void CreateConfigFromPolicy_SetsLifecycle()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = SandboxFactory.CreateConfigFromPolicy(policy);

        Assert.NotNull(config.Lifecycle);
        Assert.True(config.Lifecycle.DestroyOnExit);
        Assert.False(config.Lifecycle.PreservePolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_MapsNetworkPolicy()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy { AllowOutbound = true },
        };
        var config = SandboxFactory.CreateConfigFromPolicy(policy);

        Assert.NotNull(config.Network);
        Assert.Equal(NetworkDefaultPolicy.Allow, config.Network.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_AllowsHostFilteringWithoutAllowOutboundForHostFilteringBackend()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["example.com"],
            },
        };

        var config = SandboxFactory.CreateConfigFromPolicy(policy, "wslc");

        Assert.Equal("wslc", config.Containment!.Value.ToString());
        Assert.Contains("example.com", config.Network!.AllowedHosts!);
    }

    [Fact]
    public void CreateConfigFromPolicy_RequiresAllowOutboundForNonProcessNonHostFilteringBackend()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["example.com"],
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SandboxFactory.CreateConfigFromPolicy(policy, "appcontainer"));
        Assert.Contains("allowedHosts/blockedHosts require allowOutbound", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_DefaultDenyNetwork()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = SandboxFactory.CreateConfigFromPolicy(policy);

        Assert.NotNull(config.Network);
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_MapsUiPolicy()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Ui = new UiPolicy
            {
                AllowWindows = true,
                Clipboard = ClipboardPolicy.Read,
                AllowInputInjection = true,
            },
        };
        var config = SandboxFactory.CreateConfigFromPolicy(policy);

        Assert.NotNull(config.Ui);
        Assert.False(config.Ui.Disable); // allowWindows=true → disable=false
        Assert.Equal(ClipboardPolicy.Read, config.Ui.Clipboard);
        Assert.True(config.Ui.Injection);
    }

    [Fact]
    public void CreateConfigFromPolicy_ValidatesVersion()
    {
        var policy = new SandboxPolicy { Version = "99.0.0" };
        Assert.Throws<ArgumentException>(
            () => SandboxFactory.CreateConfigFromPolicy(policy));
    }

    [Fact]
    public void CreateConfigFromPolicy_UnknownContainment_Throws()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        Assert.Throws<NotSupportedException>(
            () => SandboxFactory.CreateConfigFromPolicy(policy, "unknown_backend"));
    }

    [Fact]
    public void InjectEnvIntoConfig_AddsToProcessEnv()
    {
        var config = new ContainerConfig
        {
            Version = "0.5.0",
            Process = new ProcessConfig { CommandLine = "echo" },
        };

        var env = new Dictionary<string, string>
        {
            ["FOO"] = "bar",
            ["PATH"] = "/usr/bin",
        };

        var result = SandboxFactory.InjectEnvIntoConfig(config, env);

        Assert.NotNull(result.Process?.Env);
        Assert.Contains("FOO=bar", result.Process.Env);
        Assert.Contains("PATH=/usr/bin", result.Process.Env);
    }

    [Fact]
    public void BuildSandboxPayload_SetsCommandLineAndCwd()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = SandboxFactory.BuildSandboxPayload("python main.py", policy, "/workspace");

        Assert.Equal("python main.py", config.Process!.CommandLine);
        Assert.Equal("/workspace", config.Process.Cwd);
    }
}

public class SpawnHelperArgvFidelityTests
{
    [Fact]
    public void ConfigBase64_IsUtf8JsonEncoded()
    {
        var config = TestConfigHelper.CreateTestConfig("echo ñ");
        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };
        var result = SpawnHelper.PrepareSpawn(config, options);

        var args = result.Args.ToList();
        var idx = args.IndexOf("--config-base64");
        var base64 = args[idx + 1];

        // Decode and verify it's valid UTF-8 JSON
        var bytes = Convert.FromBase64String(base64);
        var json = Encoding.UTF8.GetString(bytes);
        var doc = JsonDocument.Parse(json);
        var cmdLine = doc.RootElement.GetProperty("process").GetProperty("commandLine").GetString();
        Assert.Equal("echo ñ", cmdLine);
    }

    [Fact]
    public void EnvGoesIntoConfig_NotProcessStartInfo()
    {
        // Verify that env vars are in the config JSON, not set on the process
        var config = new ContainerConfig
        {
            Version = "0.5.0",
            Process = new ProcessConfig
            {
                CommandLine = "echo test",
                Env = ["FOO=bar", "BAZ=qux"],
            },
        };

        var options = new SandboxSpawnOptions { ExecutablePath = @"C:\Windows\System32\cmd.exe" };
        var result = SpawnHelper.PrepareSpawn(config, options);

        var args = result.Args.ToList();
        var idx = args.IndexOf("--config-base64");
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[idx + 1]));
        var doc = JsonDocument.Parse(json);
        var env = doc.RootElement.GetProperty("process").GetProperty("env");
        Assert.Equal(JsonValueKind.Array, env.ValueKind);
        Assert.Equal("FOO=bar", env[0].GetString());
        Assert.Equal("BAZ=qux", env[1].GetString());
    }
}

public class ProcessConnectionTests
{
    [Fact]
    public async Task Spawn_DrainsConcurrently_CapturesBothStreams()
    {
        // Spawn a process that writes to both stdout and stderr
        var conn = ProcessConnection.Spawn("cmd.exe",
            ["/c", "echo STDOUT_MARKER && echo STDERR_MARKER 1>&2"],
            null);

        var exitCode = await conn.WaitForExitAsync();

        var stdout = conn.GetStdout();
        var stderr = conn.GetStderr();

        Assert.Equal(0, exitCode);
        Assert.Contains("STDOUT_MARKER", stdout);
        Assert.Contains("STDERR_MARKER", stderr);

        conn.Dispose();
    }

    [Fact]
    public async Task Spawn_Cancellation_KillsProcess()
    {
        var conn = ProcessConnection.Spawn("cmd.exe",
            ["/c", "ping -t 127.0.0.1"],
            null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => conn.WaitForExitAsync(cts.Token));

        conn.Dispose();
    }
}

// Shared helper - accessible via 'using static' is not needed since methods above use it directly
// The CreateTestConfig usages above resolve to this via a static using-like pattern in the file.

internal static class TestConfigHelper
{
    public static ContainerConfig CreateTestConfig(string commandLine) => new()
    {
        Version = "0.5.0",
        Process = new ProcessConfig { CommandLine = commandLine },
    };
}





