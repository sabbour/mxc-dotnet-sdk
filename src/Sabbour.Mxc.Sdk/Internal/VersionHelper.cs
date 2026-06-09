// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NuGet.Versioning;

namespace Sabbour.Mxc.Sdk.Internal;

/// <summary>
/// Version validation and SDK version constant. Port of helper.ts version logic.
/// Uses NuGet.Versioning (SemanticVersion) for semver parsing/comparison —
/// faithful to the TS semver library usage.
/// </summary>
internal static class VersionHelper
{
    /// <summary>
    /// The highest policy version the SDK understands.
    /// Matches TS: <c>const SUPPORTED_VERSION = '0.7.0-alpha';</c>
    /// </summary>
    internal const string SupportedVersion = "0.7.0-alpha";

    /// <summary>
    /// The oldest policy version the SDK still accepts.
    /// Matches TS: <c>const MIN_VERSION = '0.4.0-alpha';</c>
    /// </summary>
    internal const string MinVersion = "0.4.0-alpha";

    /// <summary>
    /// Validates a policy version string. Throws if null/empty, non-semver,
    /// older than MinVersion, or newer than SupportedVersion.
    /// Comparison uses Major.Minor only (patch-agnostic), matching the TS logic.
    /// </summary>
    internal static void ValidatePolicyVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            throw new ArgumentException("Policy version is required");

        if (!SemanticVersion.TryParse(version, out var parsed))
        {
            throw new ArgumentException(
                $"Invalid policy version '{version}': must be valid semver" +
                $" (e.g., '0.5.0' or '0.5.0-alpha')");
        }

        var supported = SemanticVersion.Parse(SupportedVersion);
        var minimum = SemanticVersion.Parse(MinVersion);

        // Too old: Major.Minor below minimum
        if (parsed.Major < minimum.Major ||
            (parsed.Major == minimum.Major && parsed.Minor < minimum.Minor))
        {
            throw new ArgumentException(
                $"Policy version '{version}' is older than supported" +
                $" (min: {minimum.Major}.{minimum.Minor}.x)." +
                $" Update your config.");
        }

        // Too new: Major.Minor above supported
        if (parsed.Major > supported.Major ||
            (parsed.Major == supported.Major && parsed.Minor > supported.Minor))
        {
            throw new ArgumentException(
                $"Policy version '{version}' is newer than supported" +
                $" (max: {supported.Major}.{supported.Minor}.x)." +
                $" Upgrade the SDK.");
        }
    }
}
