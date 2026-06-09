// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Exit information from a PTY process.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="Signal">Optional termination signal (Linux/macOS). Null on Windows.</param>
public sealed record PtyExitEvent(int ExitCode, int? Signal = null);

/// <summary>
/// Options for creating a PTY connection. SDK-owned — does NOT expose any
/// Porta.Pty types. Adapters translate this internally.
/// </summary>
public sealed record PtyOptions
{
    /// <summary>Terminal type name (e.g., "xterm-color"). Default: "xterm-color".</summary>
    public string TerminalName { get; init; } = "xterm-color";

    /// <summary>Initial column count. Default: 120 (matches TS).</summary>
    public int Cols { get; init; } = 120;

    /// <summary>Initial row count. Default: 80 (matches TS).</summary>
    public int Rows { get; init; } = 80;

    /// <summary>Working directory for the spawned process.</summary>
    public string? Cwd { get; init; }
}

/// <summary>
/// SDK-owned PTY connection interface. Analogous to node-pty's IPty.
/// Does NOT expose any Porta.Pty types — fallback blast radius stays inside the adapter.
/// </summary>
public interface IPtyConnection : IAsyncDisposable, IDisposable
{
    /// <summary>OS-level process ID of the spawned child.</summary>
    int ProcessId { get; }

    /// <summary>
    /// Fires when raw data chunks are received from the PTY.
    /// Data is NOT line-buffered — callers receive raw terminal output.
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? DataReceived;

    /// <summary>
    /// Fires when the PTY process exits.
    /// </summary>
    event Action<PtyExitEvent>? Exited;

    /// <summary>Write a UTF-8 string to the PTY's stdin.</summary>
    void Write(string data);

    /// <summary>Write raw bytes to the PTY's stdin.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Resize the PTY window.</summary>
    void Resize(int columns, int rows);

    /// <summary>Send SIGKILL/TerminateProcess to the child.</summary>
    void Kill();

    /// <summary>Wait for the process to exit.</summary>
    Task<PtyExitEvent> WaitForExitAsync(CancellationToken cancellationToken = default);
}
