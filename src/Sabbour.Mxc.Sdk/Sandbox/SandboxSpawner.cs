// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Sabbour.Mxc.Sdk.Errors;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Spawns sandboxed processes via wxc-exec. Port of sandbox.ts spawn functions.
/// 
/// Methods:
/// - SpawnSandboxAsync: one-shot PTY spawn, buffers combined output, returns SandboxProcessResult.
/// - SpawnSandboxFromConfig: PTY mode → returns IPtyConnection.
/// - SpawnSandboxProcessFromConfig: Pipe mode (usePty:false) → returns ProcessConnection.
/// </summary>
public sealed class SandboxSpawner
{
    private readonly IPtyConnectionFactory _ptyFactory;
    private readonly IProcessConnectionFactory _processFactory;

    /// <summary>
    /// Creates a SandboxSpawner with production defaults (Porta.Pty + System.Diagnostics.Process).
    /// </summary>
    public SandboxSpawner()
        : this(new DefaultPtyConnectionFactory(), new DefaultProcessConnectionFactory())
    {
    }

    /// <summary>
    /// Creates a SandboxSpawner with injected factories (test seam).
    /// </summary>
    internal SandboxSpawner(IPtyConnectionFactory ptyFactory, IProcessConnectionFactory processFactory)
    {
        _ptyFactory = ptyFactory ?? throw new ArgumentNullException(nameof(ptyFactory));
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
    }

    /// <summary>
    /// One-shot async spawn: uses PTY internally, buffers combined output, returns result.
    /// Faithful to TS spawnSandboxAsync — PTY combines stdout+stderr.
    /// On nonzero exit, scans output for error envelope → throws MxcException.
    /// 
    /// Fix #8: Caps combined output buffer at MaxCombinedOutputBytes.
    /// Cancellation (.NET improvement): on cancel throws OperationCanceledException,
    /// NOT a synthetic nonzero result. TS one-shot ignores the signal.
    /// </summary>
    public async Task<SandboxProcessResult> SpawnSandboxAsync(
        ContainerConfig config,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        const int MaxCombinedOutputBytes = 4 * 1024 * 1024;
        const string TruncationMarker = "\n[mxc-sdk: output truncated at 4 MB]";

        options ??= new SandboxSpawnOptions();
        var prepared = SpawnHelper.PrepareSpawn(config, options);

        var output = new StringBuilder();
        int outputBytes = 0;
        bool truncated = false;

        await using var pty = await _ptyFactory.SpawnAsync(
            prepared.ExecutablePath, prepared.Args, options.PtyOptions, cancellationToken)
            .ConfigureAwait(false);

        pty.DataReceived += chunk =>
        {
            if (truncated) return;
            var byteCount = chunk.Length;
            if (outputBytes + byteCount > MaxCombinedOutputBytes)
            {
                truncated = true;
                output.Append(TruncationMarker);
                return;
            }
            output.Append(Encoding.UTF8.GetString(chunk.Span));
            outputBytes += byteCount;
        };

        PtyExitEvent exitEvent;
        if (cancellationToken.CanBeCanceled)
        {
            try
            {
                exitEvent = await pty.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                pty.Kill();
                throw;
            }
        }
        else
        {
            exitEvent = await pty.WaitForExitAsync().ConfigureAwait(false);
        }

        var combinedOutput = output.ToString();

        // On nonzero exit, scan for error envelope (matches TS behavior)
        if (exitEvent.ExitCode != 0)
        {
            var mxcError = SpawnHelper.TryParseErrorEnvelopeFromLines(combinedOutput);
            if (mxcError is not null)
                throw mxcError;
        }

        return new SandboxProcessResult
        {
            Stdout = combinedOutput,
            Stderr = "",
            ExitCode = exitEvent.ExitCode,
        };
    }

    /// <summary>
    /// Spawns a sandboxed process from a pre-built config.
    /// Branches on options.UsePty: true → PTY (IPtyConnection), false → pipe (ProcessConnection wrapped as IPtyConnection).
    /// Port of TS spawnSandboxFromConfig which branches on usePty flag.
    /// 
    /// Fix #1: UsePty=false is now honored — routes to pipe mode.
    /// Fix #2: Uses validating PrepareSpawn so missing commandLine fails with SDK error.
    /// </summary>
    public async Task<IPtyConnection> SpawnSandboxFromConfigAsync(
        ContainerConfig config,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SandboxSpawnOptions();
        var prepared = SpawnHelper.PrepareSpawn(config, options);

        if (!options.UsePty)
        {
            // Pipe mode: wrap ProcessConnection as IPtyConnection adapter
            var conn = _processFactory.Spawn(
                prepared.ExecutablePath, prepared.Args, options.PtyOptions?.Cwd);
            return new ProcessConnectionPtyAdapter(conn);
        }

        return await _ptyFactory.SpawnAsync(
            prepared.ExecutablePath, prepared.Args, options.PtyOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Spawns a sandboxed process from a pre-built config in pipe mode (usePty:false).
    /// Returns ProcessConnection with separate stdout/stderr streams.
    /// Explicit pipe-mode API (alternative to SpawnSandboxFromConfigAsync with UsePty=false).
    /// 
    /// Fix #2: Uses validating PrepareSpawn so missing commandLine fails with SDK error.
    /// </summary>
    public ProcessConnection SpawnSandboxProcessFromConfig(
        ContainerConfig config,
        SandboxSpawnOptions? options = null)
    {
        options ??= new SandboxSpawnOptions();
        var prepared = SpawnHelper.PrepareSpawn(config, options);

        return _processFactory.Spawn(
            prepared.ExecutablePath, prepared.Args, options.PtyOptions?.Cwd);
    }

    /// <summary>
    /// Convenience: pipe-mode one-shot that awaits exit and returns output.
    /// Faithful to TS pipe-mode (usePty:false): does NOT parse error envelopes
    /// from user output. Error-envelope line-scanning is reserved for the PTY
    /// combined-output one-shot path (SpawnSandboxAsync, matching TS spawnSandboxAsync).
    /// </summary>
    public async Task<SandboxProcessResult> SpawnSandboxProcessAsync(
        ContainerConfig config,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SandboxSpawnOptions();
        var prepared = SpawnHelper.PrepareSpawn(config, options);

        using var conn = _processFactory.Spawn(
            prepared.ExecutablePath, prepared.Args, options.PtyOptions?.Cwd);

        var exitCode = await conn.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = conn.GetStdout();
        var stderr = conn.GetStderr();

        return new SandboxProcessResult
        {
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = exitCode,
        };
    }
}

/// <summary>
/// Factory interface for creating PTY connections (test seam).
/// </summary>
internal interface IPtyConnectionFactory
{
    Task<IPtyConnection> SpawnAsync(
        string executablePath,
        IReadOnlyList<string> args,
        PtyOptions? options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory interface for creating process connections (test seam).
/// </summary>
internal interface IProcessConnectionFactory
{
    ProcessConnection Spawn(string executablePath, IReadOnlyList<string> args, string? workingDirectory);
}

/// <summary>
/// Default PTY factory backed by Porta.Pty.
/// </summary>
internal sealed class DefaultPtyConnectionFactory : IPtyConnectionFactory
{
    public async Task<IPtyConnection> SpawnAsync(
        string executablePath,
        IReadOnlyList<string> args,
        PtyOptions? options,
        CancellationToken cancellationToken = default)
    {
        return await PortaPtyConnection.SpawnAsync(executablePath, args, options, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Default process factory backed by System.Diagnostics.Process.
/// </summary>
internal sealed class DefaultProcessConnectionFactory : IProcessConnectionFactory
{
    public ProcessConnection Spawn(string executablePath, IReadOnlyList<string> args, string? workingDirectory)
    {
        return ProcessConnection.Spawn(executablePath, args, workingDirectory);
    }
}
