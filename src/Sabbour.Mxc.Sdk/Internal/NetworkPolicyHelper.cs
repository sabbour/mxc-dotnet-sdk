// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Internal;

/// <summary>
/// Applies Linux network-policy defaults to a <see cref="ContainerConfig"/>.
/// Port of helper.ts applyLinuxNetworkPolicy.
/// </summary>
internal static class NetworkPolicyHelper
{
    /// <summary>
    /// Auto-promotes enforcementMode to 'firewall' when host lists are present
    /// without a proxy. Pure logic — no I/O.
    /// </summary>
    internal static NetworkConfig? ApplyLinuxNetworkPolicy(NetworkConfig? network)
    {
        if (network is null) return null;

        // 'capabilities' has no Linux equivalent — silently ignored per TS behavior
        // (TS emits console.warn; we skip the side effect since this is pure logic).

        bool hasProxy = network.Proxy is not null;
        bool hasHostRules = (network.AllowedHosts?.Count > 0) || (network.BlockedHosts?.Count > 0);

        if (hasHostRules && !hasProxy)
        {
            return network with { EnforcementMode = NetworkEnforcementMode.Firewall };
        }

        return network;
    }
}
