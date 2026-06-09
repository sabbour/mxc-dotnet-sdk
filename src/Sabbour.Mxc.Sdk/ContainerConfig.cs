// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Main WXC configuration — the wire-format DTO sent to wxc-exec.
/// Every property uses [JsonPropertyName] to pin the exact wire casing.
/// Serialization order is controlled by <see cref="ContainerConfigConverter"/>
/// to match the TS SDK's insertion order (byte-identical JSON output).
/// </summary>
[JsonConverter(typeof(ContainerConfigConverter))]
public sealed record ContainerConfig
{
    /// <summary>MXC config schema version. Required.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Externally assigned container identifier.</summary>
    [JsonPropertyName("containerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainerId { get; init; }

    /// <summary>Containment intent (preferred) or concrete backend (override).</summary>
    [JsonPropertyName("containment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContainmentValue? Containment { get; init; }

    /// <summary>Container lifecycle settings.</summary>
    [JsonPropertyName("lifecycle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LifecycleConfig? Lifecycle { get; init; }

    /// <summary>Process execution settings.</summary>
    [JsonPropertyName("process")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessConfig? Process { get; init; }

    /// <summary>ProcessContainer configuration (Windows process-level backend).</summary>
    [JsonPropertyName("processContainer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessContainerConfig? ProcessContainer { get; init; }

    /// <summary>
    /// Legacy alias of <see cref="ProcessContainer"/>. Retained for pre-0.6 SDK migration.
    /// </summary>
    [JsonPropertyName("appContainer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use ProcessContainer instead. This alias may be removed in a future minor release.")]
    public ProcessContainerConfig? AppContainer { get; init; }

    /// <summary>LXC container configuration (Linux only).</summary>
    [JsonPropertyName("lxc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LxcConfig? Lxc { get; init; }

    /// <summary>Filesystem access configuration.</summary>
    [JsonPropertyName("filesystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilesystemConfig? Filesystem { get; init; }

    /// <summary>Network access configuration.</summary>
    [JsonPropertyName("network")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkConfig? Network { get; init; }

    /// <summary>Experimental features (only applied when --experimental flag is set).</summary>
    [JsonPropertyName("experimental")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExperimentalConfig? Experimental { get; init; }

    /// <summary>Cross-platform UI configuration.</summary>
    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiConfig? Ui { get; init; }
}

/// <summary>Process execution settings.</summary>
public sealed record ProcessConfig
{
    /// <summary>Complete command line to execute.</summary>
    [JsonPropertyName("commandLine")]
    public required string CommandLine { get; init; }

    /// <summary>Working directory for the process.</summary>
    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    /// <summary>Environment variables as KEY=VALUE strings.</summary>
    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Env { get; init; }

    /// <summary>Execution timeout in milliseconds (default: 0 = no timeout).</summary>
    [JsonPropertyName("timeout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Timeout { get; init; }
}

/// <summary>Container lifecycle settings shared across all backends.</summary>
public sealed record LifecycleConfig
{
    /// <summary>Destroy the container after execution completes (default: true).</summary>
    [JsonPropertyName("destroyOnExit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestroyOnExit { get; init; }

    /// <summary>Retain filesystem and network policies after execution (default: false).</summary>
    [JsonPropertyName("preservePolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PreservePolicy { get; init; }
}

/// <summary>ProcessContainer configuration for the Windows process-level backend.</summary>
public sealed record ProcessContainerConfig
{
    /// <summary>AppContainer profile name (default: "CLI"). Deprecated: use containerId instead.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Use least privilege mode (default: false).</summary>
    [JsonPropertyName("leastPrivilege")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LeastPrivilege { get; init; }

    /// <summary>Additional AppContainer capabilities.</summary>
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>BaseProcess-specific UI settings (Windows only).</summary>
    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BaseProcessUiConfig? Ui { get; init; }
}

/// <summary>BaseProcess-specific UI configuration (Windows only).</summary>
public sealed record BaseProcessUiConfig
{
    /// <summary>UI isolation level for the desktop.</summary>
    [JsonPropertyName("isolation")]
    public required UiIsolationLevel Isolation { get; init; }

    /// <summary>Whether desktop system control is allowed.</summary>
    [JsonPropertyName("desktopSystemControl")]
    public required bool DesktopSystemControl { get; init; }

    /// <summary>System settings access level.</summary>
    [JsonPropertyName("systemSettings")]
    public required string SystemSettings { get; init; }

    /// <summary>Whether IME (Input Method Editor) is allowed.</summary>
    [JsonPropertyName("ime")]
    public required bool Ime { get; init; }
}

/// <summary>Filesystem access configuration.</summary>
public sealed record FilesystemConfig
{
    /// <summary>Paths the script can read and write.</summary>
    [JsonPropertyName("readwritePaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ReadwritePaths { get; init; }

    /// <summary>Paths the script can read but not write.</summary>
    [JsonPropertyName("readonlyPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ReadonlyPaths { get; init; }

    /// <summary>Paths the script cannot access.</summary>
    [JsonPropertyName("deniedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeniedPaths { get; init; }

    /// <summary>Automatically remove file access policy after execution (default: true).</summary>
    [JsonPropertyName("clearPolicyOnExit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClearPolicyOnExit { get; init; }
}

/// <summary>Network access configuration.</summary>
public sealed record NetworkConfig
{
    /// <summary>Network enforcement mode.</summary>
    [JsonPropertyName("enforcementMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkEnforcementMode? EnforcementMode { get; init; }

    /// <summary>Default network policy: "allow" or "block" (default: "block").</summary>
    [JsonPropertyName("defaultPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkDefaultPolicy? DefaultPolicy { get; init; }

    /// <summary>Whether to allow inbound connections to local IP listeners (default: false).</summary>
    [JsonPropertyName("allowLocalNetwork")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowLocalNetwork { get; init; }

    /// <summary>Hostnames or IP addresses/CIDR blocks to allow.</summary>
    [JsonPropertyName("allowedHosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedHosts { get; init; }

    /// <summary>Hostnames or IP addresses to block.</summary>
    [JsonPropertyName("blockedHosts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? BlockedHosts { get; init; }

    /// <summary>Proxy configuration.</summary>
    [JsonPropertyName("proxy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProxyConfig? Proxy { get; init; }

    /// <summary>Automatically remove firewall rules after execution (default: true). Deprecated.</summary>
    [JsonPropertyName("removeRulesOnExit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Obsolete("Use lifecycle.preservePolicy instead.")]
    public bool? RemoveRulesOnExit { get; init; }
}

/// <summary>Experimental features.</summary>
public sealed record ExperimentalConfig
{
    /// <summary>WSLC SDK configuration for Linux containers from Windows.</summary>
    [JsonPropertyName("wslc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WslcConfig? Wslc { get; init; }

    /// <summary>macOS sandbox configuration (macOS only).</summary>
    [JsonPropertyName("seatbelt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SeatbeltConfig? Seatbelt { get; init; }
}

/// <summary>Cross-platform UI configuration in ContainerConfig.</summary>
public sealed record UiConfig
{
    /// <summary>Whether UI is disabled (no visible windows).</summary>
    [JsonPropertyName("disable")]
    public required bool Disable { get; init; }

    /// <summary>Clipboard access level.</summary>
    [JsonPropertyName("clipboard")]
    public required ClipboardPolicy Clipboard { get; init; }

    /// <summary>Whether input injection is allowed.</summary>
    [JsonPropertyName("injection")]
    public required bool Injection { get; init; }
}

/// <summary>WSLC SDK configuration for Linux containers from Windows.</summary>
public sealed record WslcConfig
{
    /// <summary>OCI container image name (default: "alpine:latest").</summary>
    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; init; }

    /// <summary>Storage path for WSLC session image store.</summary>
    [JsonPropertyName("storagePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoragePath { get; init; }

    /// <summary>Target OS for the container (default: "linux").</summary>
    [JsonPropertyName("targetOs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetOs { get; init; }

    /// <summary>Number of CPUs allocated to the WSLC session.</summary>
    [JsonPropertyName("cpuCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CpuCount { get; init; }

    /// <summary>Memory in MB allocated to the WSLC session.</summary>
    [JsonPropertyName("memoryMb")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MemoryMb { get; init; }

    /// <summary>Enable GPU passthrough to the container (default: false).</summary>
    [JsonPropertyName("gpu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Gpu { get; init; }

    /// <summary>Path to a local tar file to import as the container image.</summary>
    [JsonPropertyName("imageTarPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageTarPath { get; init; }

    /// <summary>Host↔container port mappings (TCP only).</summary>
    [JsonPropertyName("portMappings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PortMapping>? PortMappings { get; init; }
}

/// <summary>Port mapping for host↔container port forwarding.</summary>
public sealed record PortMapping
{
    /// <summary>Port on the Windows host.</summary>
    [JsonPropertyName("windowsPort")]
    public required int WindowsPort { get; init; }

    /// <summary>Port inside the Linux container.</summary>
    [JsonPropertyName("containerPort")]
    public required int ContainerPort { get; init; }

    /// <summary>Protocol: "tcp" or "udp" (default: "tcp").</summary>
    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; init; }
}

/// <summary>LXC container configuration for Linux sandbox.</summary>
public sealed record LxcConfig
{
    /// <summary>Container name (default: auto-generated).</summary>
    [JsonPropertyName("containerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainerName { get; init; }

    /// <summary>Linux distribution for container rootfs (default: "alpine").</summary>
    [JsonPropertyName("distribution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Distribution { get; init; }

    /// <summary>Distribution release version (default: "3.19").</summary>
    [JsonPropertyName("release")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Release { get; init; }

    /// <summary>Whether to destroy the container after execution (default: true).</summary>
    [JsonPropertyName("destroyOnExit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DestroyOnExit { get; init; }
}

/// <summary>macOS Seatbelt sandbox configuration (experimental).</summary>
public sealed record SeatbeltConfig
{
    /// <summary>Optional override of the generated TinyScheme sandbox profile.</summary>
    [JsonPropertyName("profileOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileOverride { get; init; }

    /// <summary>Allow the inner process to allocate pseudo-terminals (default: true).</summary>
    [JsonPropertyName("nestedPty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NestedPty { get; init; }

    /// <summary>Allow the inner process to use the macOS Keychain (default: false).</summary>
    [JsonPropertyName("keychainAccess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? KeychainAccess { get; init; }

    /// <summary>Additional Mach service global-names to allow mach-lookup for.</summary>
    [JsonPropertyName("extraMachLookups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ExtraMachLookups { get; init; }
}
