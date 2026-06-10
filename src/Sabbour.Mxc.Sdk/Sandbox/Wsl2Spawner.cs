// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Spawns sandboxed processes inside WSL2 using bubblewrap (bwrap) or unshare.
/// Invoked via <c>wsl.exe -- bash -c &lt;payload&gt;</c> from a Windows host.
///
/// The user command is base64-encoded before embedding in the bash payload to
/// prevent shell injection through the command string.
///
/// Verified on Ubuntu 24.04 aarch64 WSL2 (bwrap 0.9.0).
/// </summary>
internal static class Wsl2Spawner
{
    private const int StdoutMaxBytes = 4 * 1024 * 1024; // 4 MB
    private const int StderrMaxBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// Spawns a sandboxed command inside WSL2 and waits for it to exit.
    /// </summary>
    /// <param name="script">Shell command to run inside the sandbox.</param>
    /// <param name="workingDirectory">
    /// Windows absolute path to the working directory (e.g. <c>C:\my\workspace</c>).
    /// Mapped to its WSL2 /mnt/&lt;drive&gt;/... equivalent automatically.
    /// </param>
    /// <param name="backend">
    /// Which WSL2 isolation backend to use (<see cref="ContainmentBackend.WslBubblewrap"/>
    /// or <see cref="ContainmentBackend.WslUnshare"/>).
    /// </param>
    /// <param name="timeoutMs">
    /// Milliseconds to wait before killing the process. 0 = no timeout.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SandboxProcessResult"/> with stdout, stderr, and exit code.</returns>
    internal static async Task<SandboxProcessResult> SpawnAsync(
        string script,
        string workingDirectory,
        ContainmentBackend backend,
        int timeoutMs = 0,
        CancellationToken cancellationToken = default)
    {
        var workdirLinux = MapToLinuxPath(workingDirectory);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var payload = backend switch
        {
            ContainmentBackend.WslBubblewrap => BuildBwrapCommand(b64, workdirLinux),
            ContainmentBackend.WslUnshare => BuildUnshareCommand(b64, workdirLinux),
            _ => throw new ArgumentException(
                $"Unsupported WSL2 backend: {backend}. Use WslBubblewrap or WslUnshare.",
                nameof(backend))
        };

        Process? proc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(payload);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMs > 0)
                cts.CancelAfter(timeoutMs);

            proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start wsl.exe process.");

            var stdoutTask = ReadBoundedAsync(proc.StandardOutput, StdoutMaxBytes, cts.Token);
            var stderrTask = ReadBoundedAsync(proc.StandardError, StderrMaxBytes, cts.Token);

            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw;
            }

            var (stdout, _) = await stdoutTask.ConfigureAwait(false);
            var (stderr, _) = await stderrTask.ConfigureAwait(false);

            return new SandboxProcessResult
            {
                ExitCode = proc.ExitCode,
                Stdout = stdout,
                Stderr = stderr,
            };
        }
        finally
        {
            if (proc is not null && !proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
            proc?.Dispose();
        }
    }

    /// <summary>
    /// Maps a Windows absolute path to its WSL2 /mnt/&lt;drive&gt;/... equivalent.
    /// E.g. <c>C:\Users\foo\bar</c> becomes <c>/mnt/c/Users/foo/bar</c>.
    /// </summary>
    internal static string MapToLinuxPath(string windowsPath)
    {
        if (windowsPath.Length >= 3
            && windowsPath[1] == ':'
            && (windowsPath[2] == '\\' || windowsPath[2] == '/'))
        {
            var drive = char.ToLowerInvariant(windowsPath[0]);
            var rest = windowsPath[3..].Replace('\\', '/');
            return $"/mnt/{drive}/{rest}";
        }
        return windowsPath.Replace('\\', '/');
    }

    // Safely single-quotes a string for embedding in a bash command.
    private static string ShellSingleQuote(string s) =>
        "'" + s.Replace("'", "'\\''") + "'";

    // bubblewrap: confine fs to the workspace, mount /usr + /etc read-only, recreate the
    // /bin, /lib, /sbin symlinks (Ubuntu ARM64 uses usr/bin etc., no /lib64), give a
    // private /proc, /dev and /tmp, and isolate the PID namespace.
    // Verified on Ubuntu 24.04 aarch64 WSL2 (bwrap 0.9.0).
    private static string BuildBwrapCommand(string b64, string workdirLinux)
    {
        var wd = ShellSingleQuote(workdirLinux);
        return
            "exec bwrap" +
            $" --bind {wd} {wd}" +
            " --ro-bind /usr /usr" +
            " --ro-bind /etc /etc" +
            " --symlink usr/bin /bin" +
            " --symlink usr/lib /lib" +
            " --symlink usr/sbin /sbin" +
            " --proc /proc" +
            " --dev /dev" +
            " --tmpfs /tmp" +
            $" --chdir {wd}" +
            " --unshare-pid" +
            " --new-session" +
            $" -- /bin/bash -c \"$(printf %s '{b64}' | base64 -d)\"";
    }

    // unshare: user/mount/PID namespace isolation. Does not confine the filesystem to the
    // workspace, so we cd into it first.
    // Verified on Ubuntu 24.04 aarch64 WSL2.
    private static string BuildUnshareCommand(string b64, string workdirLinux)
    {
        var wd = ShellSingleQuote(workdirLinux);
        return
            $"cd {wd} && exec unshare --user --map-root-user --mount --pid --fork" +
            $" /bin/bash -c \"$(printf %s '{b64}' | base64 -d)\"";
    }

    private static async Task<(string Output, bool Truncated)> ReadBoundedAsync(
        StreamReader reader, int maxBytes, CancellationToken ct)
    {
        var buffer = new char[4096];
        var sb = new StringBuilder();
        int total = 0;
        bool truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            int remaining = maxBytes - total;
            if (remaining <= 0) { truncated = true; break; }
            int take = Math.Min(read, remaining);
            sb.Append(buffer, 0, take);
            total += take;
            if (take < read) { truncated = true; break; }
        }
        return (sb.ToString(), truncated);
    }
}
