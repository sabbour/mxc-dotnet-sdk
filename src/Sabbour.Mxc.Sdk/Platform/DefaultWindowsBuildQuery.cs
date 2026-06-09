// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Default production implementation of <see cref="IWindowsBuildQuery"/>.
/// Reads CurrentBuild and UBR from the Windows registry via the injected probe runner.
/// </summary>
internal sealed class DefaultWindowsBuildQuery : IWindowsBuildQuery
{
    private readonly IPlatformProbeRunner _probeRunner;

    public DefaultWindowsBuildQuery(IPlatformProbeRunner probeRunner)
    {
        _probeRunner = probeRunner;
    }

    /// <inheritdoc />
    public (int Major, int Minor)? GetWindowsBuild()
    {
        const string registryPath = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion";

        var currentBuild = _probeRunner.QueryRegistry(registryPath, "CurrentBuild");
        var ubrValue = _probeRunner.QueryRegistry(registryPath, "UBR");

        if (string.IsNullOrEmpty(currentBuild) || string.IsNullOrEmpty(ubrValue))
            return null;

        if (!int.TryParse(currentBuild, out var major))
            return null;

        if (!TryParseIntOrHex(ubrValue, out var minor))
            return null;

        return (major, minor);
    }

    /// <summary>
    /// Parses a string as a decimal integer or a 0x-prefixed hexadecimal integer.
    /// TS <c>Number(value)</c> accepts both forms; <c>reg query</c> returns REG_DWORD as hex (e.g. "0x2169").
    /// </summary>
    internal static bool TryParseIntOrHex(string value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
