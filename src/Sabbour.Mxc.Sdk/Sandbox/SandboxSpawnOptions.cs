// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Options for spawning a sandboxed process. Port of TS SandboxSpawnOptions.
/// </summary>
public sealed record SandboxSpawnOptions
{
    /// <summary>Enable debug output from wxc-exec (adds --debug flag).</summary>
    public bool Debug { get; init; }

    /// <summary>Enable experimental features (adds --experimental flag).</summary>
    public bool Experimental { get; init; }

    /// <summary>
    /// Explicit path to the wxc-exec (or lxc-exec) binary.
    /// When set, the SDK uses this path directly instead of searching.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Skip platform support check.</summary>
    public bool SkipPlatformCheck { get; init; }

    /// <summary>
    /// Dry run mode: parse and validate config without executing.
    /// Adds --dry-run flag.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>Directory for diagnostic log files (adds --log-file flag).</summary>
    public string? LogDir { get; init; }

    /// <summary>
    /// When false, uses pipe mode (System.Diagnostics.Process) instead of PTY.
    /// Defaults to true (uses PTY). Only meaningful for SpawnSandboxFromConfig.
    /// </summary>
    public bool UsePty { get; init; } = true;

    /// <summary>PTY-specific options (terminal name, cols, rows, cwd).</summary>
    public PtyOptions? PtyOptions { get; init; }
}
