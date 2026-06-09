// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Entry point for state-aware sandbox clients. Exposes a concrete accessor per backend
/// so callers never need to spell generic parameters.
/// </summary>
public static class StateAwareSandboxes
{
    /// <summary>
    /// Gets the IsolationSession state-aware sandbox client using the default (production) spawn runner.
    /// </summary>
    public static StateAwareSandboxClient<
        IsolationSessionBackend,
        IsolationSessionProvisionConfig,
        IsolationSessionStartConfig,
        IsolationSessionExecConfig,
        IsolationSessionStopConfig,
        IsolationSessionDeprovisionConfig,
        IsolationSessionProvisionMetadata,
        NoMetadata,
        NoMetadata,
        NoMetadata> IsolationSession { get; } = CreateIsolationSession(DefaultStateAwareSpawnRunner.Instance);

    /// <summary>
    /// Creates an IsolationSession client with a custom spawn runner (for testing).
    /// </summary>
    internal static StateAwareSandboxClient<
        IsolationSessionBackend,
        IsolationSessionProvisionConfig,
        IsolationSessionStartConfig,
        IsolationSessionExecConfig,
        IsolationSessionStopConfig,
        IsolationSessionDeprovisionConfig,
        IsolationSessionProvisionMetadata,
        NoMetadata,
        NoMetadata,
        NoMetadata> CreateIsolationSession(IStateAwareSpawnRunner runner)
    {
        return new StateAwareSandboxClient<
            IsolationSessionBackend,
            IsolationSessionProvisionConfig,
            IsolationSessionStartConfig,
            IsolationSessionExecConfig,
            IsolationSessionStopConfig,
            IsolationSessionDeprovisionConfig,
            IsolationSessionProvisionMetadata,
            NoMetadata,
            NoMetadata,
            NoMetadata>(
            runner,
            MxcJsonContext.Default.IsolationSessionProvisionConfig,
            MxcJsonContext.Default.IsolationSessionStartConfig,
            MxcJsonContext.Default.IsolationSessionExecConfig,
            MxcJsonContext.Default.IsolationSessionStopConfig,
            MxcJsonContext.Default.IsolationSessionDeprovisionConfig,
            MxcJsonContext.Default.IsolationSessionProvisionMetadata);
    }
}
