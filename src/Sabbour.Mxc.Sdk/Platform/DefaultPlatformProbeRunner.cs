// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Default production implementation of <see cref="IPlatformProbeRunner"/>.
/// Actually shells out to external binaries and reads the registry.
///
/// Process-safety: all external process launches drain both stdout and stderr
/// asynchronously (via ReadToEndAsync on both streams before WaitForExit) to
/// prevent pipe-buffer deadlocks. A configurable timeout kills hung processes.
/// </summary>
internal sealed class DefaultPlatformProbeRunner : IPlatformProbeRunner
{
    /// <summary>Maximum bytes to capture per stream (stdout/stderr). Prevents OOM from chatty processes.</summary>
    private const int MaxOutputBytes = 4 * 1024 * 1024; // 4 MB

    /// <inheritdoc />
    public string RunProbe()
    {
        var wxcPath = FindWxcExecutable();
        if (wxcPath is null)
        {
            throw new InvalidOperationException("wxc-exec not found");
        }

        var result = RunCommand(wxcPath, ["--probe"], timeoutMs: 5000);

        // TS uses execFileSync which throws on nonzero exit → probe fields left unset.
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"wxc-exec --probe exited with code {result.ExitCode}");
        }

        return result.Stdout;
    }

    /// <inheritdoc />
    public ProcessResult RunCommand(string command, IReadOnlyList<string> arguments, int timeoutMs = 10000)
    {
        using var process = new Process();
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList (not Arguments string) for injection safety.
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        process.StartInfo = startInfo;
        process.Start();

        // Drain both stdout and stderr asynchronously to prevent pipe-buffer deadlocks.
        var stdoutTask = ReadStreamCapped(process.StandardOutput, MaxOutputBytes);
        var stderrTask = ReadStreamCapped(process.StandardError, MaxOutputBytes);

        // Wait for streams to drain first (avoids the deadlock that WaitForExit alone can cause).
        Task.WaitAll([stdoutTask, stderrTask], timeoutMs + 1000);

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"{command} timed out after {timeoutMs}ms");
        }

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <inheritdoc />
    public bool IsToolAvailable(string command, string arguments)
    {
        try
        {
            // Split arguments string for ArgumentList usage.
            var args = SplitArguments(arguments);
            var result = RunCommand(command, args, timeoutMs: 10000);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string? QueryRegistry(string key, string valueName)
    {
        try
        {
            var result = RunCommand("reg", ["query", key, "/v", valueName], timeoutMs: 5000);

            if (result.ExitCode != 0)
                return null;

            // Parse reg query output format:
            //     ValueName    REG_SZ    Value
            foreach (var line in result.Stdout.Split('\n'))
            {
                if (line.Contains(valueName, StringComparison.OrdinalIgnoreCase))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"REG_\w+\s+(.+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsToolAvailableInWsl2(string toolName)
    {
        try
        {
            var result = RunWsl2Command($"command -v {toolName}", timeoutMs: 8000);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public ProcessResult RunWsl2Command(string bashCommand, int timeoutMs = 10000)
    {
        return RunCommand("wsl.exe", ["--", "bash", "-c", bashCommand], timeoutMs);
    }

    /// <summary>
    /// Find the wxc-exec executable.
    ///
    /// <para><b>Trust boundary note:</b> MXC_BIN_DIR and PATH are caller-controlled
    /// (trusted) environment variables. Package/assembly-relative paths are preferred
    /// first to reduce PATH-hijack exposure. The search order is:
    /// <list type="number">
    /// <item>MXC_BIN_DIR/&lt;arch&gt;/ (explicit override)</item>
    /// <item>Assembly-relative bin/&lt;arch&gt;/ (NuGet package content root)</item>
    /// <item>AppContext.BaseDirectory/bin/&lt;arch&gt;/ (publish layout)</item>
    /// <item>Dev/repo Cargo target paths (monorepo development equivalent to TS)</item>
    /// <item>PATH (last fallback, .NET-distribution-specific addition)</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static string? FindWxcExecutable() => FindExecutable("wxc-exec");

    /// <summary>
    /// Find the lxc-exec executable (Linux containment backend).
    /// Port of platform.ts findLxcExecutable.
    /// </summary>
    internal static string? FindLxcExecutable() => FindExecutable("lxc-exec");

    /// <summary>
    /// Find the mxc-exec-mac executable (macOS seatbelt containment backend).
    /// Port of platform.ts findSeatbeltExecutable.
    /// </summary>
    internal static string? FindSeatbeltExecutable() => FindExecutable("mxc-exec-mac");

    private static string? FindExecutable(string baseName)
    {
        var arch = GetSdkArch();
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var exeName = $"{baseName}{ext}";

        // 1. MXC_BIN_DIR override (TS: process.env.MXC_BIN_DIR)
        var binDir = Environment.GetEnvironmentVariable("MXC_BIN_DIR");
        if (!string.IsNullOrEmpty(binDir))
        {
            var overridePath = Path.Combine(binDir, arch, exeName);
            if (File.Exists(overridePath))
                return overridePath;
        }

        // 2. Assembly-relative bin/<arch>/ (NuGet package content root)
        // Equivalent to TS: path.join(pkgRoot, 'bin', getSdkArch(), 'wxc-exec.exe')
        var assemblyDir = Path.GetDirectoryName(typeof(DefaultPlatformProbeRunner).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var pkgBinPath = Path.Combine(assemblyDir, "bin", arch, exeName);
            if (File.Exists(pkgBinPath))
                return pkgBinPath;
        }

        // 3. AppContext.BaseDirectory/bin/<arch>/ (publish layout)
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var baseBinPath = Path.Combine(baseDir, "bin", arch, exeName);
            if (File.Exists(baseBinPath))
                return baseBinPath;
        }

        // 4. Dev/repo Cargo target paths (monorepo development, matches TS)
        var targetTriple = GetRustTargetTriple();
        var repoTargetDir = FindRepoTargetDir();
        if (repoTargetDir is not null)
        {
            string[] devPaths =
            [
                Path.Combine(repoTargetDir, targetTriple, "release", exeName),
                Path.Combine(repoTargetDir, targetTriple, "debug", exeName),
                Path.Combine(repoTargetDir, "release", exeName),
                Path.Combine(repoTargetDir, "debug", exeName),
            ];

            foreach (var devPath in devPaths)
            {
                if (File.Exists(devPath))
                    return devPath;
            }
        }

        // 5. PATH (last fallback — .NET-distribution-specific addition, not in TS)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string GetSdkArch()
    {
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    }

    /// <summary>
    /// Get the Rust target triple for the current platform, matching TS getRustTargetTriple().
    /// </summary>
    private static string GetRustTargetTriple()
    {
        var arch = RuntimeInformation.OSArchitecture;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return arch == Architecture.Arm64 ? "aarch64-unknown-linux-gnu" : "x86_64-unknown-linux-gnu";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return arch == Architecture.Arm64 ? "aarch64-apple-darwin" : "x86_64-apple-darwin";
        // Windows
        return arch == Architecture.Arm64 ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc";
    }

    /// <summary>
    /// Attempts to locate the repo 'src/target' directory by walking up from the assembly location.
    /// Equivalent to TS: path.join(pkgRoot, '..', 'src', 'target')
    /// </summary>
    private static string? FindRepoTargetDir()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(DefaultPlatformProbeRunner).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDir))
            return null;

        // Walk up looking for a 'src/target' directory (monorepo layout)
        var dir = new DirectoryInfo(assemblyDir);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "src", "target");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Reads from a StreamReader with a byte cap to avoid OOM from excessive output.
    /// </summary>
    private static async Task<string> ReadStreamCapped(StreamReader reader, int maxBytes)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        var totalBytes = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            var byteCount = Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (totalBytes + byteCount > maxBytes)
            {
                var remaining = maxBytes - totalBytes;
                if (remaining > 0)
                {
                    sb.Append(buffer, 0, Math.Min(read, remaining));
                }
                break;
            }

            sb.Append(buffer, 0, read);
            totalBytes += byteCount;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Simple argument splitting for legacy IsToolAvailable(string, string) calls.
    /// Splits on whitespace, respecting that our known arguments are simple constants.
    /// </summary>
    private static string[] SplitArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
