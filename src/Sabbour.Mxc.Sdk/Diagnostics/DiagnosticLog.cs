// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sabbour.Mxc.Sdk.Internal;

namespace Sabbour.Mxc.Sdk.Diagnostics;

/// <summary>
/// Diagnostic logging for the MXC SDK.
/// When the MXC diagnostic console is running, sends log messages over
/// a named pipe. Best-effort: if the console is not running the message
/// is silently dropped. Enabled by environment variable MXC_DIAG_CONSOLE=1.
/// </summary>
public sealed class DiagnosticLog : IDisposable
{
    /// <summary>Default maximum message length for diagnostic log messages.</summary>
    public const int DefaultMaxMessageLength = 8192;

    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private bool _pipeAttempted;
    private bool _pipeConnected;
    private readonly bool _enabled;
    private readonly object _lock = new();
    private readonly int MaxMessageLength;
    private readonly Func<string, string>? _redact;

    /// <param name="maxMessageLength">Maximum message length (0 = unlimited).</param>
    /// <param name="redact">Optional redaction hook applied before writing.
    /// When null, wamToken redaction is applied by default (R3 security requirement).</param>
    public DiagnosticLog(int maxMessageLength = DefaultMaxMessageLength, Func<string, string>? redact = null)
    {
        var envVal = Environment.GetEnvironmentVariable("MXC_DIAG_CONSOLE");
        _enabled = envVal == "1" || string.Equals(envVal, "true", StringComparison.OrdinalIgnoreCase);
        _pipeName = GetDiagnosticPipeName();
        MaxMessageLength = maxMessageLength;
        // R3: Default to wamToken redaction when no custom hook is provided
        _redact = redact ?? TokenRedactor.Redact;
    }

    /// <summary>
    /// Send a diagnostic log message to the MXC diagnostic console.
    /// Messages are best-effort and non-blocking. Size-capped and redaction-aware.
    /// </summary>
    public void Log(string message)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            var pipe = EnsurePipe();
            if (pipe is null) return;

            try
            {
                // Size cap: truncate messages to avoid unbounded pipe writes
                var capped = message.Length > MaxMessageLength
                    ? message[..MaxMessageLength] + "...[truncated]"
                    : message;

                // Apply redaction hook if configured
                if (_redact is not null)
                {
                    capped = _redact(capped);
                }

                var envelope = JsonSerializer.Serialize(new { msg = $"[SDK] {capped}" }) + "\n";
                var bytes = Encoding.UTF8.GetBytes(envelope);
                pipe.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // Best-effort: ignore write errors.
                DisconnectPipe();
            }
        }
    }

    /// <summary>Close the diagnostic pipe connection.</summary>
    public void Close()
    {
        lock (_lock)
        {
            DisconnectPipe();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    private NamedPipeClientStream? EnsurePipe()
    {
        if (_pipeConnected && _pipe is not null)
            return _pipe;

        if (_pipeAttempted)
            return null;

        // Only supported on Windows.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _pipeAttempted = true;
            return null;
        }

        _pipeAttempted = true;

        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            _pipe.Connect(timeout: 100);
            _pipeConnected = true;
            return _pipe;
        }
        catch
        {
            DisconnectPipe();
            return null;
        }
    }

    private void DisconnectPipe()
    {
        _pipeConnected = false;
        if (_pipe is not null)
        {
            try { _pipe.Dispose(); } catch { /* ignore */ }
            _pipe = null;
        }
    }

    /// <summary>
    /// Compute the per-user pipe name including current user's SID on Windows.
    /// Falls back to the base name if SID cannot be determined.
    /// </summary>
    private static string GetDiagnosticPipeName()
    {
        const string baseName = "mxc-diagnostics";

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return baseName;

        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (!string.IsNullOrEmpty(sid))
                return $"{baseName}-{sid}";
        }
        catch
        {
            // Best-effort: fall back to base name.
        }

        return baseName;
    }
}
