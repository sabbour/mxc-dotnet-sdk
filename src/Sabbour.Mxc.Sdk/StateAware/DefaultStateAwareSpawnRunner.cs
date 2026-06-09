// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using Sabbour.Mxc.Sdk.Sandbox;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Production spawn runner that resolves wxc-exec and invokes it.
/// R2: Uses the same 4 MB stdout/stderr read caps from P4 (ProcessConnection.MaxOutputBytes).
/// Drains stderr concurrently to avoid deadlock.
/// </summary>
internal sealed class DefaultStateAwareSpawnRunner : IStateAwareSpawnRunner
{
    internal static readonly DefaultStateAwareSpawnRunner Instance = new();

    public async Task<SandboxProcessResult> SpawnAndCollectAsync(
        string envelopeJson,
        SandboxSpawnOptions options,
        CancellationToken cancellationToken = default)
    {
        var prepared = SpawnHelper.PrepareSpawnFromJson(envelopeJson, options);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = prepared.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in prepared.Args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();

        // R2: Drain both streams concurrently with the same 4 MB cap from P4.
        var stdoutTask = ReadCappedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadCappedAsync(process.StandardError, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new SandboxProcessResult
        {
            Stdout = await stdoutTask.ConfigureAwait(false),
            Stderr = await stderrTask.ConfigureAwait(false),
            ExitCode = process.ExitCode,
        };
    }

    /// <summary>
    /// Reads a stream up to ProcessConnection.MaxOutputBytes (4 MB), then truncates.
    /// Reuses the same cap constant from P4's ProcessConnection to avoid duplicating magic numbers.
    /// </summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int totalBytes = 0;
        bool truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read == 0) break;

            ct.ThrowIfCancellationRequested();

            var byteCount = Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (totalBytes + byteCount > ProcessConnection.MaxOutputBytes)
            {
                var remaining = ProcessConnection.MaxOutputBytes - totalBytes;
                if (remaining > 0)
                {
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
            sb.Append(ProcessConnection.TruncationMarker);
        }

        return sb.ToString();
    }

    public IPtyConnection SpawnStreaming(string envelopeJson, SandboxSpawnOptions options)
    {
        var prepared = SpawnHelper.PrepareSpawnFromJson(envelopeJson, options);
        var ptyOpts = options.PtyOptions ?? new PtyOptions();

        // SpawnAsync returns Task<PortaPtyConnection>; block here since the TS
        // equivalent (pty.spawn) is also synchronous from the caller's perspective.
        return PortaPtyConnection.SpawnAsync(
            prepared.ExecutablePath,
            prepared.Args,
            ptyOpts).GetAwaiter().GetResult();
    }
}
