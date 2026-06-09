// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>Result of provisioning a sandbox.</summary>
/// <typeparam name="TBackend">Backend marker.</typeparam>
/// <typeparam name="TMetadata">Provision metadata type.</typeparam>
public sealed record ProvisionResult<TBackend, TMetadata>
    where TBackend : IStateAwareBackend
{
    /// <summary>Branded sandbox id minted by the backend.</summary>
    public required SandboxId<TBackend> SandboxId { get; init; }

    /// <summary>Optional provision-time metadata from the backend.</summary>
    public TMetadata? Metadata { get; init; }
}

/// <summary>Result of a lifecycle phase (start/stop/deprovision).</summary>
/// <typeparam name="TMetadata">Phase metadata type.</typeparam>
public sealed record PhaseResult<TMetadata>
{
    /// <summary>Optional metadata from the backend for this phase.</summary>
    public TMetadata? Metadata { get; init; }
}

/// <summary>Result of a buffered exec call.</summary>
public sealed record ExecResult
{
    /// <summary>Standard output from the script.</summary>
    public required string Stdout { get; init; }

    /// <summary>Standard error from the script.</summary>
    public required string Stderr { get; init; }

    /// <summary>Process exit code.</summary>
    public required int ExitCode { get; init; }
}
