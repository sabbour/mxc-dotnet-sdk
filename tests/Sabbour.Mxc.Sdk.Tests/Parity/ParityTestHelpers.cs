// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sabbour.Mxc.Sdk.Platform;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

internal static class ParityTestHelpers
{
    public static string? PlatformSkip
    {
        get
        {
            try
            {
                var support = new PlatformProber().GetPlatformSupport();
                return support.IsSupported ? null : "MXC not supported on this machine";
            }
            catch
            {
                return "MXC not supported on this machine";
            }
        }
    }

    public static SandboxSpawnOptions TestOptions(Func<SandboxSpawnOptions, SandboxSpawnOptions>? configure = null)
    {
        var options = new SandboxSpawnOptions
        {
            Experimental = true,
            ExecutablePath = CurrentProcessExecutablePath(),
        };

        return configure?.Invoke(options) ?? options;
    }

    public static ContainerConfig TestConfig(string commandLine = "echo test") => new()
    {
        Version = "0.5.0",
        Process = new ProcessConfig { CommandLine = commandLine },
    };

    public static FakeStateAwareSpawnRunner FakeSpawn(FakeChildOptions? options = null)
    {
        return new FakeStateAwareSpawnRunner(options ?? new FakeChildOptions());
    }

    public static JsonElement? TryDecodeConfigBase64Envelope(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (!string.Equals(args[i], "--config-base64", StringComparison.Ordinal))
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[i + 1]));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        return null;
    }

    private static string CurrentProcessExecutablePath()
    {
        if (!string.IsNullOrEmpty(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        using var current = Process.GetCurrentProcess();
        var modulePath = current.MainModule?.FileName;
        if (!string.IsNullOrEmpty(modulePath) && File.Exists(modulePath))
        {
            return modulePath;
        }

        return typeof(object).Assembly.Location;
    }
}

internal sealed record FakeChildOptions
{
    public string Stdout { get; init; } = "";

    public string Stderr { get; init; } = "";

    public int ExitCode { get; init; }

    public Exception? Error { get; init; }
}

internal sealed record SpawnCapture
{
    public string? Command { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    public string? EnvelopeJson { get; init; }

    public JsonElement? Envelope { get; init; }

    public SandboxSpawnOptions? Options { get; init; }

    public PtyOptions? PtyOptions { get; init; }
}

internal sealed class FakePtyConnection : IPtyConnection
{
    private const int KillExitCode = -1;
    private readonly object _gate = new();
    private readonly TaskCompletionSource<PtyExitEvent> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FakeChildOptions? _autoComplete;
    private readonly List<string> _writes = [];
    private readonly List<(int Columns, int Rows)> _resizes = [];
    private bool _autoCompleteStarted;

    public FakePtyConnection(FakeChildOptions? autoComplete = null)
    {
        _autoComplete = autoComplete;
    }

    public int ProcessId { get; init; } = 12345;

    public event Action<ReadOnlyMemory<byte>>? DataReceived;

    public event Action<PtyExitEvent>? Exited;

    public IReadOnlyList<string> Writes => _writes;

    public IReadOnlyList<(int Columns, int Rows)> Resizes => _resizes;

    public int KillCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public void Write(string data) => _writes.Add(data);

    public void Write(ReadOnlySpan<byte> data) => _writes.Add(Encoding.UTF8.GetString(data));

    public void Resize(int columns, int rows) => _resizes.Add((columns, rows));

    public void Kill()
    {
        KillCount++;
        CompleteExit(KillExitCode);
    }

    public Task<PtyExitEvent> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        StartAutoCompleteIfNeeded();
        return cancellationToken.CanBeCanceled ? _exit.Task.WaitAsync(cancellationToken) : _exit.Task;
    }

    public void EmitData(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        DataReceived?.Invoke(bytes);
    }

    public void Complete(FakeChildOptions options)
    {
        if (options.Error is not null)
        {
            _exit.TrySetException(options.Error);
            return;
        }

        if (options.Stdout.Length > 0)
        {
            EmitData(options.Stdout);
        }

        if (options.Stderr.Length > 0)
        {
            EmitData(options.Stderr);
        }

        CompleteExit(options.ExitCode);
    }

    public void CompleteExit(int exitCode, int? signal = null)
    {
        var exitEvent = new PtyExitEvent(exitCode, signal);
        if (_exit.TrySetResult(exitEvent))
        {
            Exited?.Invoke(exitEvent);
        }
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    private void StartAutoCompleteIfNeeded()
    {
        if (_autoComplete is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_autoCompleteStarted)
            {
                return;
            }

            _autoCompleteStarted = true;
        }

        _ = Task.Run(() => Complete(_autoComplete));
    }
}

internal sealed class FakePtyConnectionFactory : IPtyConnectionFactory
{
    private readonly Queue<FakePtyConnection> _connections = new();
    private readonly List<SpawnCapture> _captures = [];
    private readonly FakeChildOptions? _defaultAutoComplete;

    public FakePtyConnectionFactory(FakeChildOptions? defaultAutoComplete = null)
    {
        _defaultAutoComplete = defaultAutoComplete;
    }

    public IReadOnlyList<SpawnCapture> Captures => _captures;

    public SpawnCapture? LastCapture => _captures.Count == 0 ? null : _captures[^1];

    public FakePtyConnection? LastConnection { get; private set; }

    public void Enqueue(FakePtyConnection connection) => _connections.Enqueue(connection);

    public Task<IPtyConnection> SpawnAsync(
        string executablePath,
        IReadOnlyList<string> args,
        PtyOptions? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var argsCopy = args.ToArray();
        _captures.Add(new SpawnCapture
        {
            Command = executablePath,
            Args = argsCopy,
            Envelope = ParityTestHelpers.TryDecodeConfigBase64Envelope(argsCopy),
            PtyOptions = options,
        });

        LastConnection = _connections.Count > 0
            ? _connections.Dequeue()
            : new FakePtyConnection(_defaultAutoComplete);

        return Task.FromResult<IPtyConnection>(LastConnection);
    }
}

internal sealed class FakeStateAwareSpawnRunner : IStateAwareSpawnRunner
{
    private readonly Queue<FakeChildOptions> _responses = new();
    private readonly List<SpawnCapture> _captures = [];

    public FakeStateAwareSpawnRunner(FakeChildOptions? firstResponse = null)
    {
        if (firstResponse is not null)
        {
            _responses.Enqueue(firstResponse);
        }
    }

    public IReadOnlyList<SpawnCapture> Captures => _captures;

    public SpawnCapture? LastCapture => _captures.Count == 0 ? null : _captures[^1];

    public FakePtyConnection StreamingConnection { get; set; } = new();

    public void Enqueue(FakeChildOptions response) => _responses.Enqueue(response);

    public Task<SandboxProcessResult> SpawnAndCollectAsync(
        string envelopeJson,
        SandboxSpawnOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Capture(envelopeJson, options);

        var response = _responses.Count > 0 ? _responses.Dequeue() : new FakeChildOptions();
        if (response.Error is not null)
        {
            return Task.FromException<SandboxProcessResult>(response.Error);
        }

        return Task.FromResult(new SandboxProcessResult
        {
            Stdout = response.Stdout,
            Stderr = response.Stderr,
            ExitCode = response.ExitCode,
        });
    }

    public IPtyConnection SpawnStreaming(string envelopeJson, SandboxSpawnOptions options)
    {
        Capture(envelopeJson, options);
        return StreamingConnection;
    }

    private void Capture(string envelopeJson, SandboxSpawnOptions options)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        _captures.Add(new SpawnCapture
        {
            EnvelopeJson = envelopeJson,
            Envelope = doc.RootElement.Clone(),
            Options = options,
        });
    }
}

internal sealed class CancellableStateAwareSpawnRunner : IStateAwareSpawnRunner
{
    private readonly TaskCompletionSource<SandboxProcessResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SpawnCapture> _captures = [];

    public IReadOnlyList<SpawnCapture> Captures => _captures;

    public SpawnCapture? LastCapture => _captures.Count == 0 ? null : _captures[^1];

    public bool CancellationObserved { get; private set; }

    public IPtyConnection StreamingConnection { get; set; } = new FakePtyConnection();

    public void Complete(FakeChildOptions options)
    {
        if (options.Error is not null)
        {
            _completion.TrySetException(options.Error);
            return;
        }

        _completion.TrySetResult(new SandboxProcessResult
        {
            Stdout = options.Stdout,
            Stderr = options.Stderr,
            ExitCode = options.ExitCode,
        });
    }

    public async Task<SandboxProcessResult> SpawnAndCollectAsync(
        string envelopeJson,
        SandboxSpawnOptions options,
        CancellationToken cancellationToken = default)
    {
        Capture(envelopeJson, options);
        try
        {
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CancellationObserved = true;
            throw;
        }
    }

    public IPtyConnection SpawnStreaming(string envelopeJson, SandboxSpawnOptions options)
    {
        Capture(envelopeJson, options);
        return StreamingConnection;
    }

    private void Capture(string envelopeJson, SandboxSpawnOptions options)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        _captures.Add(new SpawnCapture
        {
            EnvelopeJson = envelopeJson,
            Envelope = doc.RootElement.Clone(),
            Options = options,
        });
    }
}

internal sealed class FakePlatformProbeRunner : IPlatformProbeRunner
{
    private readonly Dictionary<string, ProcessResult> _commandResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _commandErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Command, IReadOnlyList<string> Arguments, int TimeoutMs)> _commands = [];

    public string? ProbeStdout { get; set; }

    public Exception? ProbeError { get; set; }

    public IReadOnlyList<(string Command, IReadOnlyList<string> Arguments, int TimeoutMs)> Commands => _commands;

    public string RunProbe()
    {
        if (ProbeError is not null)
        {
            throw ProbeError;
        }

        return ProbeStdout ?? throw new InvalidOperationException("Probe output was not configured.");
    }

    public ProcessResult RunCommand(string command, IReadOnlyList<string> arguments, int timeoutMs = 10000)
    {
        var argsCopy = arguments.ToArray();
        _commands.Add((command, argsCopy, timeoutMs));

        var key = CommandKey(command, argsCopy);
        if (_commandErrors.TryGetValue(key, out var error))
        {
            throw error;
        }

        return _commandResults.TryGetValue(key, out var result)
            ? result
            : new ProcessResult(1, "", "");
    }

    public bool IsToolAvailable(string command, string arguments)
    {
        return _tools.TryGetValue(ToolKey(command, arguments), out var available) && available;
    }

    public bool FileExists(string path) => _existingFiles.Contains(path);

    public string? QueryRegistry(string key, string valueName)
    {
        return _registry.TryGetValue(RegistryKey(key, valueName), out var value) ? value : null;
    }

    private readonly Dictionary<string, bool> _wsl2Tools = new(StringComparer.OrdinalIgnoreCase);
    private ProcessResult? _wsl2CommandResult;

    public bool IsToolAvailableInWsl2(string toolName)
    {
        return _wsl2Tools.TryGetValue(toolName, out var available) && available;
    }

    public ProcessResult RunWsl2Command(string bashCommand, int timeoutMs = 10000)
    {
        return _wsl2CommandResult ?? new ProcessResult(1, "", "");
    }

    public FakePlatformProbeRunner WithWsl2ToolAvailable(string toolName, bool available = true)
    {
        _wsl2Tools[toolName] = available;
        return this;
    }

    public FakePlatformProbeRunner WithWsl2CommandResult(ProcessResult result)
    {
        _wsl2CommandResult = result;
        return this;
    }

    public FakePlatformProbeRunner WithProbeStdout(string stdout)
    {
        ProbeStdout = stdout;
        ProbeError = null;
        return this;
    }

    public FakePlatformProbeRunner WithProbeError(Exception error)
    {
        ProbeError = error;
        return this;
    }

    public FakePlatformProbeRunner WithCommandResult(string command, IReadOnlyList<string> arguments, ProcessResult result)
    {
        _commandResults[CommandKey(command, arguments)] = result;
        _commandErrors.Remove(CommandKey(command, arguments));
        return this;
    }

    public FakePlatformProbeRunner WithCommandError(string command, IReadOnlyList<string> arguments, Exception error)
    {
        _commandErrors[CommandKey(command, arguments)] = error;
        _commandResults.Remove(CommandKey(command, arguments));
        return this;
    }

    public FakePlatformProbeRunner WithDismFeatureState(string state)
    {
        return WithCommandResult(
            "dism",
            ["/online", "/get-featureinfo", "/featurename:Containers-DisposableClientVM"],
            new ProcessResult(0, $"Feature Name : Containers-DisposableClientVM{Environment.NewLine}State : {state}{Environment.NewLine}", ""));
    }

    public FakePlatformProbeRunner WithToolAvailable(string command, string arguments, bool available = true)
    {
        _tools[ToolKey(command, arguments)] = available;
        return this;
    }

    public FakePlatformProbeRunner WithFileExists(string path, bool exists = true)
    {
        if (exists)
        {
            _existingFiles.Add(path);
        }
        else
        {
            _existingFiles.Remove(path);
        }

        return this;
    }

    public FakePlatformProbeRunner WithRegistryValue(string key, string valueName, string? value)
    {
        _registry[RegistryKey(key, valueName)] = value;
        return this;
    }

    private static string CommandKey(string command, IReadOnlyList<string> arguments)
    {
        return $"{command}\0{string.Join('\u001f', arguments)}";
    }

    private static string ToolKey(string command, string arguments) => $"{command}\0{arguments}";

    private static string RegistryKey(string key, string valueName) => $"{key}\0{valueName}";
}

internal sealed class FakeWindowsBuildQuery : IWindowsBuildQuery
{
    public FakeWindowsBuildQuery((int Major, int Minor)? build = null)
    {
        Build = build;
    }

    public (int Major, int Minor)? Build { get; set; }

    public (int Major, int Minor)? GetWindowsBuild() => Build;
}

// PARITY-GAP: TypeScript fakeSpawn can replace every child_process.spawn call with
// synthetic stdout/stderr streams. The current C# pipe-mode seam (IProcessConnectionFactory)
// returns concrete ProcessConnection, whose constructor is private, so a fully synthetic
// pipe-mode ProcessConnection cannot be built without adding a production hook. Use
// FakeStateAwareSpawnRunner for state-aware child_process parity and FakePtyConnectionFactory
// for node-pty parity; pipe-mode tests that need separate synthetic stdout/stderr still need
// a future production seam.
// PARITY-GAP: node child.kill() closes with a null exit code. IPtyConnection exposes an int
// PtyExitEvent.ExitCode, so FakePtyConnection reports -1 when Kill() completes the fake.
