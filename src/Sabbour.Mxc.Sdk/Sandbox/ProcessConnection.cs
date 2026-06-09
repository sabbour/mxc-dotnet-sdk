// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Process-based connection (pipe mode, usePty:false branch).
/// Wraps System.Diagnostics.Process with concurrent stdout/stderr drain.
///
/// Fix #3: Uses process.WaitForExitAsync instead of _exitTcs to avoid race/hang
///         when a fast process exits before event handler attaches.
/// Fix #6: Removed raw StandardOutput/StandardError Stream properties that raced
///         with internal ReadToEndAsync drain. Callers get buffered output via
///         GetStdout()/GetStderr() after WaitForExitAsync completes.
/// Fix #8: Output buffers are capped at MaxOutputBytes to prevent memory DoS.
/// </summary>
public sealed class ProcessConnection : IAsyncDisposable, IDisposable
{
    /// <summary>Maximum bytes buffered per stream (stdout/stderr). ~4 MB.</summary>
    internal const int MaxOutputBytes = 4 * 1024 * 1024;
    internal const string TruncationMarker = "\n[mxc-sdk: output truncated at 4 MB]";

    private readonly Process _process;
    private readonly Task<string> _stdoutTask;
    private readonly Task<string> _stderrTask;
    private volatile bool _disposed;

    /// <summary>OS-level process ID.</summary>
    public int ProcessId => _process.Id;

    private ProcessConnection(Process process, Task<string> stdoutTask, Task<string> stderrTask)
    {
        _process = process;
        _stdoutTask = stdoutTask;
        _stderrTask = stderrTask;
    }

    /// <summary>
    /// Spawns a process in pipe mode (no PTY). Drains stdout and stderr concurrently.
    /// </summary>
    internal static ProcessConnection Spawn(
        string executablePath,
        IReadOnlyList<string> args,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        // Close stdin immediately — config goes via argv, not stdin (match TS)
        process.StandardInput.Close();

        // Start BOTH capped read tasks BEFORE awaiting exit to prevent pipe-buffer deadlock
        var stdoutTask = ReadCappedAsync(process.StandardOutput);
        var stderrTask = ReadCappedAsync(process.StandardError);

        return new ProcessConnection(process, stdoutTask, stderrTask);
    }

    /// <summary>
    /// Reads a stream up to MaxOutputBytes, then truncates with a marker.
    /// Prevents unbounded memory allocation from noisy processes.
    /// </summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int totalBytes = 0;
        bool truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read == 0) break;

            var byteCount = Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (totalBytes + byteCount > MaxOutputBytes)
            {
                // Append only what fits
                var remaining = MaxOutputBytes - totalBytes;
                if (remaining > 0)
                {
                    // Approximate char count from remaining bytes
                    var charsToTake = Math.Min(read, remaining);
                    sb.Append(buffer, 0, (int)charsToTake);
                }
                truncated = true;
                break;
            }

            sb.Append(buffer, 0, read);
            totalBytes += byteCount;
        }

        if (truncated)
        {
            sb.Append(TruncationMarker);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Waits for the process to exit and returns exit code.
    /// Fix #3: Uses process.WaitForExitAsync to avoid race with fast-exiting processes.
    /// On cancellation: kills the process, finishes draining, throws.
    /// </summary>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken == default)
        {
            // Drain both streams, then wait for process exit (no race — WaitForExitAsync is reliable)
            await Task.WhenAll(_stdoutTask, _stderrTask).ConfigureAwait(false);
            await _process.WaitForExitAsync().ConfigureAwait(false);
            return _process.ExitCode;
        }

        try
        {
            using var reg = cancellationToken.Register(() =>
            {
                try { _process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            });

            await Task.WhenAll(_stdoutTask, _stderrTask).WaitAsync(cancellationToken).ConfigureAwait(false);
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return _process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Ensure process is dead
            try { _process.Kill(entireProcessTree: true); }
            catch { /* already dead */ }

            // Wait briefly for drain to complete
            await Task.WhenAny(Task.WhenAll(_stdoutTask, _stderrTask), Task.Delay(2000)).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets buffered stdout (available after WaitForExitAsync completes).</summary>
    internal string GetStdout() => _stdoutTask.IsCompletedSuccessfully ? _stdoutTask.Result : "";

    /// <summary>Gets buffered stderr (available after WaitForExitAsync completes).</summary>
    internal string GetStderr() => _stderrTask.IsCompletedSuccessfully ? _stderrTask.Result : "";

    /// <summary>Kill the process tree immediately.</summary>
    public void Kill()
    {
        if (_disposed) return;
        try { _process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    /// <summary>Kills the process and releases resources synchronously.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process.Kill(entireProcessTree: true); } catch { }
        _process.Dispose();
    }

    /// <summary>Kills the process and releases resources, awaiting stream drain completion.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process.Kill(entireProcessTree: true); } catch { }
        // Wait for drain tasks
        try { await Task.WhenAny(Task.WhenAll(_stdoutTask, _stderrTask), Task.Delay(2000)).ConfigureAwait(false); }
        catch { }
        _process.Dispose();
    }
}
