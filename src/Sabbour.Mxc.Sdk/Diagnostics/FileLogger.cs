// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk.Internal;

namespace Sabbour.Mxc.Sdk.Diagnostics;

/// <summary>
/// Appends timestamped log lines to a file.
/// Emits Console.Error warning if the file cannot be opened, then degrades to no-op.
/// Faithful port of the TypeScript FileLogger.
/// </summary>
public sealed class FileLogger : IMxcLogger, IDisposable
{
    /// <summary>Default maximum length for logged message + data combined.</summary>
    public const int DefaultMaxMessageLength = 8192;

    private StreamWriter? _writer;
    private readonly object _lock = new();
    private readonly int _maxMessageLength;
    private readonly Func<string, string>? _redact;

    /// <summary>
    /// Creates a file logger that appends to the given path.
    /// </summary>
    /// <param name="filePath">Destination log file path.</param>
    /// <param name="maxMessageLength">Maximum character length for message + data. 0 = unlimited.</param>
    /// <param name="redact">Optional redaction hook applied to each log line before writing.
    /// When null, wamToken redaction is applied by default (R3 security requirement).</param>
    public FileLogger(string filePath, int maxMessageLength = DefaultMaxMessageLength, Func<string, string>? redact = null)
    {
        _maxMessageLength = maxMessageLength;
        // R3: Default to wamToken redaction when no custom hook is provided
        _redact = redact ?? TokenRedactor.Redact;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
        catch (Exception ex)
        {
            // Log only ex.Message (not the full exception) and avoid emitting the full path
            var fileName = Path.GetFileName(filePath);
            Console.Error.WriteLine($"[mxc-sdk] Could not open log file '{fileName}': {ex.Message}");
            _writer = null;
        }
    }

    /// <summary>Writes a timestamped, level-tagged, redacted log line to the file.</summary>
    public void Log(MxcLogLevel level, string message, IReadOnlyDictionary<string, object>? data = null)
    {
        lock (_lock)
        {
            if (_writer is null) return;

            try
            {
                // TS: new Date().toISOString() → millisecond precision, trailing Z
                var ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                var levelStr = level switch
                {
                    MxcLogLevel.Info => "INFO",
                    MxcLogLevel.Warn => "WARN",
                    MxcLogLevel.Error => "ERROR",
                    _ => "INFO"
                };
                var suffix = data is not null ? " " + JsonSerializer.Serialize(data) : "";
                var content = $"{message}{suffix}";

                // Apply size cap
                if (_maxMessageLength > 0 && content.Length > _maxMessageLength)
                {
                    content = content[.._maxMessageLength] + "...[truncated]";
                }

                var line = $"[{ts}] {levelStr} {content}";

                // Apply redaction hook
                if (_redact is not null)
                {
                    line = _redact(line);
                }

                _writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                // On write failure, log only ex.Message; never throw from logging
                try
                {
                    Console.Error.WriteLine($"[mxc-sdk] Log write error: {ex.Message}");
                }
                catch { /* truly best-effort */ }
            }
        }
    }

    /// <summary>Flushes and closes the underlying file stream. Safe to call multiple times.</summary>
    public void Close()
    {
        lock (_lock)
        {
            if (_writer is not null)
            {
                try { _writer.Dispose(); }
                catch { /* ignore */ }
                _writer = null;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Close();
}
