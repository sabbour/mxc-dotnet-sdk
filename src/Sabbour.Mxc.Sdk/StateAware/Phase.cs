// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Lifecycle phase constants for state-aware sandbox requests.
/// Each value corresponds to a stage in the isolation-session lifecycle.
/// </summary>
public static class Phase
{
    /// <summary>Provision phase — creates and allocates the sandbox environment.</summary>
    public const string Provision = "provision";

    /// <summary>Start phase — boots the previously provisioned sandbox.</summary>
    public const string Start = "start";

    /// <summary>Exec phase — runs a command inside a started sandbox.</summary>
    public const string Exec = "exec";

    /// <summary>Stop phase — halts the sandbox without releasing provisioned resources.</summary>
    public const string Stop = "stop";

    /// <summary>Deprovision phase — tears down and releases all sandbox resources.</summary>
    public const string Deprovision = "deprovision";
}
