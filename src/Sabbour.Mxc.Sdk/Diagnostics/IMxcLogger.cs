// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Diagnostics;

/// <summary>
/// Logging abstraction for the MXC SDK. Named IMxcLogger to avoid collision
/// with Microsoft.Extensions.Logging.ILogger.
/// </summary>
public interface IMxcLogger
{
    /// <summary>Log at the specified level.</summary>
    void Log(MxcLogLevel level, string message, IReadOnlyDictionary<string, object>? data = null);

    /// <summary>Close/flush the logger. Safe to call multiple times.</summary>
    void Close();
}

/// <summary>
/// Log levels matching the TypeScript source.
/// </summary>
public enum MxcLogLevel
{
    /// <summary>Informational messages.</summary>
    Info,
    /// <summary>Warning messages indicating potential issues.</summary>
    Warn,
    /// <summary>Error messages indicating failures.</summary>
    Error
}
