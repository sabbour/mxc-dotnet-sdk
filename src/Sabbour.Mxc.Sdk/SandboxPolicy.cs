// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// The main sandbox policy configuration interface for external consumers.
/// Policy describes what the caller wants restricted. Cross-platform.
/// Omitted fields = most restrictive (default-deny).
/// </summary>
public sealed record SandboxPolicy
{
    /// <summary>Policy version (semver). Must match a supported schema version.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Filesystem access restrictions.</summary>
    [JsonPropertyName("filesystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilesystemPolicy? Filesystem { get; init; }

    /// <summary>Network access restrictions.</summary>
    [JsonPropertyName("network")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkPolicy? Network { get; init; }

    /// <summary>UI access restrictions.</summary>
    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiPolicy? Ui { get; init; }

    /// <summary>Execution timeout in milliseconds. Omitted = no timeout.</summary>
    [JsonPropertyName("timeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutMs { get; init; }
}

/// <summary>
/// Filesystem access restrictions in a SandboxPolicy.
/// </summary>
public sealed record FilesystemPolicy
{
    /// <summary>Paths granted read and write access.</summary>
    [JsonPropertyName("readwritePaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ReadwritePaths { get; init; }

    /// <summary>Paths granted read-only access.</summary>
    [JsonPropertyName("readonlyPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ReadonlyPaths { get; init; }

    /// <summary>Paths explicitly denied all access.</summary>
    [JsonPropertyName("deniedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeniedPaths { get; init; }

    /// <summary>Whether to clear the filesystem policy when the shell exits. (default: true)</summary>
    [JsonPropertyName("clearPolicyOnExit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClearPolicyOnExit { get; init; }
}

/// <summary>
/// Network access restrictions in a SandboxPolicy.
/// </summary>
public sealed record NetworkPolicy
{
    /// <summary>Whether to allow outbound connections to the Internet. (default: false)</summary>
    [JsonPropertyName("allowOutbound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowOutbound { get; init; }

    /// <summary>Whether to allow connections to local networks. (default: false)</summary>
    [JsonPropertyName("allowLocalNetwork")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowLocalNetwork { get; init; }

    /// <summary>When set, ONLY these outbound hosts are reachable.</summary>
    [JsonPropertyName("allowedHosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedHosts { get; init; }

    /// <summary>Hosts to block even when outbound is allowed.</summary>
    [JsonPropertyName("blockedHosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? BlockedHosts { get; init; }

    /// <summary>Proxy configuration. Routes all traffic through this proxy.</summary>
    [JsonPropertyName("proxy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProxyConfig? Proxy { get; init; }
}

/// <summary>
/// UI access restrictions in a SandboxPolicy.
/// </summary>
public sealed record UiPolicy
{
    /// <summary>Whether the sandbox may create visible windows. (default: false)</summary>
    [JsonPropertyName("allowWindows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowWindows { get; init; }

    /// <summary>Clipboard access level. (default: "none")</summary>
    [JsonPropertyName("clipboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipboardPolicy? Clipboard { get; init; }

    /// <summary>Whether the sandbox may inject keyboard/mouse input. (default: false)</summary>
    [JsonPropertyName("allowInputInjection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowInputInjection { get; init; }
}

/// <summary>
/// Proxy configuration. Discriminated union: exactly one of builtinTestServer, localhost, or url.
/// TS: <c>{ builtinTestServer: true } | { localhost: number } | { url: string }</c>.
/// Use factory methods to construct; the converter enforces one-of serialization.
/// </summary>
[JsonConverter(typeof(ProxyConfigConverter))]
public abstract record ProxyConfig
{
    private ProxyConfig() { }

    /// <summary>Use the built-in test proxy server.</summary>
    public static ProxyConfig BuiltinTestServer() => new BuiltinTestServerProxy();

    /// <summary>Route traffic via a localhost proxy on the specified port.</summary>
    public static ProxyConfig Localhost(int port) => new LocalhostProxy(port);

    /// <summary>Route traffic via the specified proxy URL.</summary>
    public static ProxyConfig Url(string url) => new UrlProxy(url ?? throw new ArgumentNullException(nameof(url)));

    /// <summary>Represents { builtinTestServer: true }.</summary>
    public sealed record BuiltinTestServerProxy() : ProxyConfig;

    /// <summary>Represents { localhost: port }.</summary>
    public sealed record LocalhostProxy(int Port) : ProxyConfig;

    /// <summary>Represents { url: "..." }.</summary>
    public sealed record UrlProxy(string ProxyUrl) : ProxyConfig;
}
