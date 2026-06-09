// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Marker interface for state-aware sandbox backends.
/// Each backend declares its wire name and sandbox-id prefix via static abstract members.
/// </summary>
public interface IStateAwareBackend
{
    /// <summary>Wire-format backend key (e.g. "isolation_session").</summary>
    static abstract string WireName { get; }

    /// <summary>
    /// Prefix segment in sandbox ids produced by this backend (e.g. "iso").
    /// The full id is "{prefix}:{unique-part}".
    /// </summary>
    static abstract string SandboxIdPrefix { get; }
}
