// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Policy;

/// <summary>
/// Options for <see cref="PolicyDiscovery.GetAvailableToolsPolicy"/>.
/// </summary>
public sealed record ToolsPolicyOptions
{
    /// <summary>
    /// When set to <c>"processcontainer"</c>, directories whose ACLs already grant
    /// access to ALL_APPLICATION_PACKAGES are excluded from the result
    /// because AppContainer processes can already see them implicitly.
    /// </summary>
    public string? ContainerType { get; init; }
}
