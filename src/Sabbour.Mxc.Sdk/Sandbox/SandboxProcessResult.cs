// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Result of a one-shot sandbox spawn (SpawnSandboxAsync).
/// Matches TS: { stdout, stderr, exitCode }.
/// For PTY mode, stderr is always "" since PTY combines all output.
/// </summary>
public sealed record SandboxProcessResult
{
    /// <summary>Combined output (stdout for pipe, combined PTY output for PTY mode).</summary>
    public required string Stdout { get; init; }

    /// <summary>Stderr output (pipe mode only; empty for PTY mode).</summary>
    public required string Stderr { get; init; }

    /// <summary>Process exit code.</summary>
    public required int ExitCode { get; init; }
}
