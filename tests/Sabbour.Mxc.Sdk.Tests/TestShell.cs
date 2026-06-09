// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Sabbour.Mxc.Sdk.Sandbox;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Cross-platform helpers for spawn tests. The SDK's spawn path validates
/// <see cref="SandboxSpawnOptions.ExecutablePath"/> with <c>File.Exists</c> and the
/// real-spawn tests launch a host shell, so tests must use an executable and command
/// syntax that exist on the current OS rather than hardcoding <c>cmd.exe</c>.
/// </summary>
internal static class TestShell
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Full path to a host shell that is guaranteed to exist on disk. Use this anywhere a
    /// test needs <see cref="SandboxSpawnOptions.ExecutablePath"/> to pass the spawn path's
    /// <c>File.Exists</c> check (the arg-building tests never actually launch it).
    /// </summary>
    public static string ExistingExecutablePath { get; } = IsWindows
        ? Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe")
        : "/bin/sh";

    /// <summary>The run-command flag for the host shell: <c>/c</c> on Windows, <c>-c</c> elsewhere.</summary>
    public static string RunFlag => IsWindows ? "/c" : "-c";

    /// <summary>
    /// Translates a shell command into the (executable, argv) pair for the host shell.
    /// </summary>
    public static (string Executable, string[] Args) Command(string command) =>
        (ExistingExecutablePath, [RunFlag, command]);

    /// <summary>Spawns the given shell command via <see cref="ProcessConnection"/> on the host shell.</summary>
    public static ProcessConnection Spawn(string command, string? workingDirectory = null)
    {
        var (exe, args) = Command(command);
        return ProcessConnection.Spawn(exe, args, workingDirectory);
    }

    /// <summary>
    /// A command that prints <paramref name="text"/> then exits with <paramref name="exitCode"/>,
    /// using the chaining/quoting syntax of the host shell. On POSIX the text is single-quoted so
    /// JSON-like payloads are emitted literally.
    /// </summary>
    public static string PrintThenExit(string text, int exitCode) => IsWindows
        ? $"echo {text}& exit /b {exitCode}"
        : $"echo '{text}'; exit {exitCode}";

    /// <summary>A command that runs effectively forever, used to test cancellation/kill.</summary>
    public static string LongRunningCommand => IsWindows ? "ping -t 127.0.0.1" : "sleep 60";
}
