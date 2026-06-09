// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Policy;

/// <summary>
/// A composable fragment of filesystem policy.
/// Callers merge one or more fragments into <see cref="FilesystemPolicy"/>.
/// </summary>
public sealed record FilesystemPolicyResult
{
    /// <summary>Paths that should be granted read-only access inside the container.</summary>
    public IReadOnlyList<string> ReadonlyPaths { get; init; } = [];

    /// <summary>Paths that should be granted read-write access inside the container.</summary>
    public IReadOnlyList<string> ReadwritePaths { get; init; } = [];
}
