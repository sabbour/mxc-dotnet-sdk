// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Adapts a ProcessConnection (pipe mode) to IPtyConnection interface.
/// Used when SpawnSandboxFromConfigAsync is called with UsePty=false,
/// allowing a unified return type while routing to pipe mode internally.
///
/// Combined stdout+stderr is emitted through DataReceived (interleaved).
/// </summary>
internal sealed class ProcessConnectionPtyAdapter : IPtyConnection
{
    private readonly ProcessConnection _conn;
    private readonly Task _drainTask;

    public int ProcessId => _conn.ProcessId;

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<PtyExitEvent>? Exited;

    internal ProcessConnectionPtyAdapter(ProcessConnection conn)
    {
        _conn = conn;
        // Start draining and forwarding output events
        _drainTask = DrainAndForwardAsync();
    }

    private async Task DrainAndForwardAsync()
    {
        var exitCode = await _conn.WaitForExitAsync().ConfigureAwait(false);

        // Emit buffered output as data events
        var stdout = _conn.GetStdout();
        if (!string.IsNullOrEmpty(stdout))
        {
            DataReceived?.Invoke(Encoding.UTF8.GetBytes(stdout));
        }

        var stderr = _conn.GetStderr();
        if (!string.IsNullOrEmpty(stderr))
        {
            DataReceived?.Invoke(Encoding.UTF8.GetBytes(stderr));
        }

        Exited?.Invoke(new PtyExitEvent(exitCode));
    }

    public void Write(string data)
    {
        // Pipe mode: stdin is closed. Writes are no-ops (match TS behavior).
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        // Pipe mode: stdin is closed.
    }

    public void Resize(int columns, int rows)
    {
        // No-op for pipe mode
    }

    public void Kill() => _conn.Kill();

    public async Task<PtyExitEvent> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        var exitCode = await _conn.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new PtyExitEvent(exitCode);
    }

    public void Dispose() => _conn.Dispose();

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
