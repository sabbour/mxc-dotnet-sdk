// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Sabbour.Mxc.Sdk.Sandbox;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Abstraction over the spawn boundary for state-aware lifecycle calls.
/// Production implementation calls wxc-exec; tests inject a fake.
/// </summary>
internal interface IStateAwareSpawnRunner
{
    /// <summary>
    /// Spawns the executor with the given envelope JSON (buffered, collect stdout/stderr).
    /// </summary>
    Task<SandboxProcessResult> SpawnAndCollectAsync(
        string envelopeJson,
        SandboxSpawnOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spawns the executor with the given envelope JSON in PTY/streaming mode.
    /// </summary>
    IPtyConnection SpawnStreaming(
        string envelopeJson,
        SandboxSpawnOptions options);
}
