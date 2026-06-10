// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Structured result from an external process invocation.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Abstraction over running the native probe binary (wxc-exec --probe).
/// Replaces the TS mutable global <c>_setProbeRunner</c> seam with constructor injection.
/// </summary>
internal interface IPlatformProbeRunner
{
    /// <summary>
    /// Executes the probe and returns its stdout.
    /// Throws if the binary is not found, times out, or exits with a nonzero code.
    /// </summary>
    string RunProbe();

    /// <summary>
    /// Runs a command with the given arguments and returns structured output.
    /// Both stdout and stderr are drained asynchronously; a timeout kills the process.
    /// </summary>
    /// <param name="command">Executable to launch.</param>
    /// <param name="arguments">Arguments (each element is a separate arg).</param>
    /// <param name="timeoutMs">Maximum wait time in milliseconds.</param>
    /// <returns>Structured result with exit code, stdout, and stderr.</returns>
    ProcessResult RunCommand(string command, IReadOnlyList<string> arguments, int timeoutMs = 10000);

    /// <summary>
    /// Checks whether a command-line tool is available by running it with the given arguments.
    /// Returns true if the process exits successfully (exit code 0).
    /// </summary>
    bool IsToolAvailable(string command, string arguments);

    /// <summary>
    /// Checks whether a file exists at the given path.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Queries the Windows registry for a value.
    /// </summary>
    /// <param name="key">Registry key path (e.g., "HKLM\Software\...").</param>
    /// <param name="valueName">Name of the value to query.</param>
    /// <returns>The registry value as a string, or null if not found.</returns>
    string? QueryRegistry(string key, string valueName);

    /// <summary>
    /// Checks whether a tool is available inside WSL2 by running
    /// <c>wsl.exe -- bash -c "command -v &lt;tool&gt;"</c>.
    /// Returns true if the command exits with code 0.
    /// </summary>
    /// <param name="toolName">The tool to probe (e.g. "bwrap", "unshare").</param>
    bool IsToolAvailableInWsl2(string toolName);

    /// <summary>
    /// Runs a bash command inside WSL2 via <c>wsl.exe -- bash -c &lt;bashCommand&gt;</c>
    /// and returns the structured result.
    /// </summary>
    /// <param name="bashCommand">The bash command string to execute.</param>
    /// <param name="timeoutMs">Maximum wait time in milliseconds.</param>
    ProcessResult RunWsl2Command(string bashCommand, int timeoutMs = 10000);
}
