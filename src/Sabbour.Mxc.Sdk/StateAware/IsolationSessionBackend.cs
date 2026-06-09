// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Backend marker for the IsolationSession state-aware backend.
/// Pass <see cref="Instance"/> as the containment parameter to facade methods.
/// </summary>
public sealed class IsolationSessionBackend : IStateAwareBackend
{
    private IsolationSessionBackend() { }

    /// <summary>
    /// Singleton marker instance used as the containment parameter in facade methods.
    /// Mirrors the TS <c>"isolation_session"</c> containment union member.
    /// </summary>
    public static IsolationSessionBackend Instance { get; } = new();

    /// <inheritdoc/>
    public static string WireName => "isolation_session";

    /// <inheritdoc/>
    public static string SandboxIdPrefix => "iso";
}
