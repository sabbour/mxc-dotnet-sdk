// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Porta.Pty;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// IPtyConnection adapter backed by Porta.Pty (ConPTY on Windows, forkpty on Unix).
/// Translates SDK-owned PtyOptions → Porta.Pty internally.
/// No Porta.Pty types are exposed in any public API.
///
/// Fix #4: Buffers data internally until a consumer subscribes (replay on subscribe).
/// Fix #8: Caps buffered output at MaxBufferedBytes to prevent memory DoS.
/// Fix #9: Cancellation uses .WaitAsync(ct) instead of poisoning shared TCS.
/// </summary>
internal sealed class PortaPtyConnection : IPtyConnection
{
    /// <summary>Maximum bytes buffered before a consumer subscribes. ~4 MB.</summary>
    internal const int MaxBufferedBytes = 4 * 1024 * 1024;

    private readonly Porta.Pty.IPtyConnection _pty;
    private readonly Task _readerTask;
    private readonly TaskCompletionSource<PtyExitEvent> _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;

    // Buffer for early output before consumer subscribes
    private readonly object _bufferLock = new();
    private List<ReadOnlyMemory<byte>>? _bufferedChunks = new();
    private int _bufferedBytes;
    private bool _bufferTruncated;

    public int ProcessId => _pty.Pid;

    private Action<ReadOnlyMemory<byte>>? _dataReceived;
    public event Action<ReadOnlyMemory<byte>>? DataReceived
    {
        add
        {
            lock (_bufferLock)
            {
                _dataReceived += value;
                // Replay buffered data to new subscriber
                if (_bufferedChunks is not null && value is not null)
                {
                    foreach (var chunk in _bufferedChunks)
                    {
                        value(chunk);
                    }
                    if (_bufferTruncated)
                    {
                        var marker = Encoding.UTF8.GetBytes("\n[mxc-sdk: PTY output buffer truncated at 4 MB]");
                        value(new ReadOnlyMemory<byte>(marker));
                    }
                    // Release buffer once a consumer subscribes
                    _bufferedChunks = null;
                }
            }
        }
        remove
        {
            lock (_bufferLock)
            {
                _dataReceived -= value;
            }
        }
    }

    public event Action<PtyExitEvent>? Exited;

    private PortaPtyConnection(Porta.Pty.IPtyConnection pty)
    {
        _pty = pty;
        _pty.ProcessExited += OnProcessExited;
        _readerTask = ReadLoopAsync();
    }

    /// <summary>
    /// Spawns a new PTY process.
    /// </summary>
    internal static async Task<PortaPtyConnection> SpawnAsync(
        string executablePath,
        IReadOnlyList<string> args,
        PtyOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new PtyOptions();

        var portaOpts = new Porta.Pty.PtyOptions
        {
            App = executablePath,
            CommandLine = args.ToArray(),
            Cols = options.Cols,
            Rows = options.Rows,
            Cwd = options.Cwd ?? Environment.CurrentDirectory,
            Name = options.TerminalName,
        };

        var pty = await PtyProvider.SpawnAsync(portaOpts, cancellationToken).ConfigureAwait(false);
        return new PortaPtyConnection(pty);
    }

    public void Write(string data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetBytes(data);
        _pty.WriterStream.Write(bytes, 0, bytes.Length);
        _pty.WriterStream.Flush();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pty.WriterStream.Write(data);
        _pty.WriterStream.Flush();
    }

    public void Resize(int columns, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pty.Resize(columns, rows);
    }

    public void Kill()
    {
        if (_disposed) return;
        try { _pty.Kill(); } catch { /* best effort */ }
    }

    /// <summary>
    /// Wait for process exit. Fix #9: cancellation applies to the *awaiting call*
    /// via .WaitAsync(ct) — does NOT cancel the shared completion source.
    /// Other waiters still get the exit event.
    /// </summary>
    public Task<PtyExitEvent> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken == default)
            return _exitTcs.Task;

        // Cancel only the caller's wait, not the shared TCS
        return _exitTcs.Task.WaitAsync(cancellationToken);
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs e)
    {
        var evt = new PtyExitEvent(e.ExitCode);
        _exitTcs.TrySetResult(evt);
        Exited?.Invoke(evt);
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var bytesRead = await _pty.ReaderStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (bytesRead <= 0) break;

                var chunk = new ReadOnlyMemory<byte>(buffer.AsSpan(0, bytesRead).ToArray());

                lock (_bufferLock)
                {
                    if (_dataReceived is not null)
                    {
                        // Consumer attached — deliver directly
                        _dataReceived(chunk);
                    }
                    else if (_bufferedChunks is not null && !_bufferTruncated)
                    {
                        // No consumer yet — buffer (with cap)
                        if (_bufferedBytes + bytesRead <= MaxBufferedBytes)
                        {
                            _bufferedChunks.Add(chunk);
                            _bufferedBytes += bytesRead;
                        }
                        else
                        {
                            _bufferTruncated = true;
                        }
                    }
                }
            }
        }
        catch (ObjectDisposedException) { /* expected on dispose */ }
        catch (IOException) { /* pipe closed */ }

        // Ensure exit event fires even if process exited without event
        _exitTcs.TrySetResult(new PtyExitEvent(_pty.ExitCode));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _pty.ProcessExited -= OnProcessExited;
        try { _pty.Kill(); } catch { /* best effort */ }

        // Wait for reader to complete
        try { await _readerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { /* timeout or already done */ }

        if (_pty is IDisposable d) d.Dispose();
        _exitTcs.TrySetResult(new PtyExitEvent(-1));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pty.ProcessExited -= OnProcessExited;
        try { _pty.Kill(); } catch { /* best effort */ }
        if (_pty is IDisposable d) d.Dispose();
        _exitTcs.TrySetResult(new PtyExitEvent(-1));
    }
}
