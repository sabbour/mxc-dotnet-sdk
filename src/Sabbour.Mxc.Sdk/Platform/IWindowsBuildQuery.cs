// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Provides the Windows build number (CurrentBuild.UBR).
/// Replaces the TS mutable global <c>_setWindowsBuildQuery</c> seam with constructor injection.
/// </summary>
internal interface IWindowsBuildQuery
{
    /// <summary>
    /// Returns the Windows build as (major, minor) or null if unavailable/unparseable.
    /// </summary>
    (int Major, int Minor)? GetWindowsBuild();
}
