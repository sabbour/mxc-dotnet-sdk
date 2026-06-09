// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Abstract containment intent — names the kind of isolation the caller wants.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContainmentType>))]
public enum ContainmentType
{
    /// <summary>Process-level isolation (Windows AppContainer / Linux namespace).</summary>
    [JsonStringEnumMemberName("process")]
    Process,

    /// <summary>Full virtual-machine isolation.</summary>
    [JsonStringEnumMemberName("vm")]
    Vm,

    /// <summary>Lightweight micro-VM isolation (e.g. Hyper-V micro-VM).</summary>
    [JsonStringEnumMemberName("microvm")]
    Microvm
}

/// <summary>
/// Concrete containment backend. Each value names a specific runner implementation in the native binary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContainmentBackend>))]
public enum ContainmentBackend
{
    /// <summary>Windows process-container backend (AppContainer + job object).</summary>
    [JsonStringEnumMemberName("processcontainer")]
    ProcessContainer,

    /// <summary>Windows Sandbox (full desktop sandbox).</summary>
    [JsonStringEnumMemberName("windows_sandbox")]
    WindowsSandbox,

    /// <summary>WSL-based container backend (WSLc).</summary>
    [JsonStringEnumMemberName("wslc")]
    Wslc,

    /// <summary>Linux LXC container backend.</summary>
    [JsonStringEnumMemberName("lxc")]
    Lxc,

    /// <summary>Hyper-V micro-VM backend.</summary>
    [JsonStringEnumMemberName("microvm")]
    Microvm,

    /// <summary>Hyperlight micro-VM backend.</summary>
    [JsonStringEnumMemberName("hyperlight")]
    Hyperlight,

    /// <summary>macOS Seatbelt (sandbox-exec) backend.</summary>
    [JsonStringEnumMemberName("seatbelt")]
    Seatbelt,

    /// <summary>Cloud-hosted isolation session backend.</summary>
    [JsonStringEnumMemberName("isolation_session")]
    IsolationSession,

    /// <summary>Linux Bubblewrap (bwrap) namespace backend.</summary>
    [JsonStringEnumMemberName("bubblewrap")]
    Bubblewrap
}

/// <summary>
/// Containment values that require the --experimental flag.
/// </summary>
public static class ExperimentalBackends
{
    /// <summary>Set of all containment backends that require the --experimental flag.</summary>
    public static IReadOnlySet<ContainmentBackend> All { get; } = new HashSet<ContainmentBackend>
    {
        ContainmentBackend.Microvm,
        ContainmentBackend.WindowsSandbox,
        ContainmentBackend.Hyperlight,
        ContainmentBackend.Wslc,
        ContainmentBackend.Seatbelt,
        ContainmentBackend.IsolationSession
    };

    // Wire strings for experimental ContainmentType values (microvm is both a type and backend)
    private static readonly HashSet<string> s_experimentalWireStrings = BuildExperimentalSet();

    private static HashSet<string> BuildExperimentalSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        // All experimental backends as wire strings
        foreach (var cb in All)
        {
            set.Add(System.Text.Json.JsonSerializer.Serialize(cb).Trim('"'));
        }

        // ContainmentType "microvm" is both an intent and a backend — ensure it's included
        set.Add("microvm");

        // Legacy aliases that resolve to experimental backends
        foreach (var (alias, backend) in LegacyContainmentAliases.Map)
        {
            if (All.Contains(backend))
            {
                set.Add(alias);
            }
        }

        return set;
    }

    /// <summary>
    /// Determines whether a containment wire string (type, backend, or legacy alias) requires the
    /// --experimental flag. Normalizes legacy aliases before checking.
    /// </summary>
    public static bool RequiresExperimental(string containment)
    {
        if (string.IsNullOrEmpty(containment))
            return false;

        return s_experimentalWireStrings.Contains(containment);
    }
}

/// <summary>
/// Runtime list of <see cref="ContainmentType"/> values.
/// </summary>
public static class ContainmentTypes
{
    /// <summary>Ordered list of all <see cref="ContainmentType"/> members.</summary>
    public static IReadOnlyList<ContainmentType> All { get; } =
        [ContainmentType.Process, ContainmentType.Vm, ContainmentType.Microvm];
}

/// <summary>
/// Deprecated containment wire values mapped to their canonical backend replacement.
/// </summary>
internal static class LegacyContainmentAliases
{
    public static IReadOnlyDictionary<string, ContainmentBackend> Map { get; } =
        new Dictionary<string, ContainmentBackend>(StringComparer.OrdinalIgnoreCase)
        {
            ["appcontainer"] = ContainmentBackend.ProcessContainer,
            ["macos_sandbox"] = ContainmentBackend.Seatbelt
        };
}

/// <summary>
/// Clipboard access policy levels.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClipboardPolicy>))]
public enum ClipboardPolicy
{
    /// <summary>No clipboard access.</summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>Read-only clipboard access.</summary>
    [JsonStringEnumMemberName("read")]
    Read,

    /// <summary>Write-only clipboard access.</summary>
    [JsonStringEnumMemberName("write")]
    Write,

    /// <summary>Full clipboard access (read + write).</summary>
    [JsonStringEnumMemberName("all")]
    All
}

/// <summary>
/// Isolation tier selected by the runtime fallback detector.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IsolationTier>))]
public enum IsolationTier
{
    /// <summary>Base container tier — minimal process isolation.</summary>
    [JsonStringEnumMemberName("base-container")]
    BaseContainer,

    /// <summary>AppContainer with boundary-descriptor/BFS filesystem enforcement.</summary>
    [JsonStringEnumMemberName("appcontainer-bfs")]
    AppContainerBfs,

    /// <summary>AppContainer with DACL-based filesystem enforcement.</summary>
    [JsonStringEnumMemberName("appcontainer-dacl")]
    AppContainerDacl
}

/// <summary>
/// Network enforcement mode.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NetworkEnforcementMode>))]
public enum NetworkEnforcementMode
{
    /// <summary>Network rules enforced via capability restrictions only.</summary>
    [JsonStringEnumMemberName("capabilities")]
    Capabilities,

    /// <summary>Network rules enforced via firewall rules only.</summary>
    [JsonStringEnumMemberName("firewall")]
    Firewall,

    /// <summary>Network rules enforced via both capabilities and firewall.</summary>
    [JsonStringEnumMemberName("both")]
    Both
}

/// <summary>
/// Network default policy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NetworkDefaultPolicy>))]
public enum NetworkDefaultPolicy
{
    /// <summary>Allow all network traffic by default.</summary>
    [JsonStringEnumMemberName("allow")]
    Allow,

    /// <summary>Block all network traffic by default.</summary>
    [JsonStringEnumMemberName("block")]
    Block
}

/// <summary>
/// BaseProcess UI isolation level.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UiIsolationLevel>))]
public enum UiIsolationLevel
{
    /// <summary>Separate Win32 desktop for UI isolation.</summary>
    [JsonStringEnumMemberName("desktop")]
    Desktop,

    /// <summary>Handle-table isolation only.</summary>
    [JsonStringEnumMemberName("handles")]
    Handles,

    /// <summary>Atom-table isolation only.</summary>
    [JsonStringEnumMemberName("atoms")]
    Atoms,

    /// <summary>Full container-level UI isolation.</summary>
    [JsonStringEnumMemberName("container")]
    Container
}

/// <summary>
/// Deprecated alias. Prefer <see cref="ContainmentType"/> or <see cref="ContainmentBackend"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SandboxingMethod>))]
public enum SandboxingMethod
{
    /// <summary>Process-level isolation.</summary>
    [JsonStringEnumMemberName("process")]
    Process,

    /// <summary>Full virtual-machine isolation.</summary>
    [JsonStringEnumMemberName("vm")]
    Vm,

    /// <summary>Lightweight micro-VM isolation.</summary>
    [JsonStringEnumMemberName("microvm")]
    Microvm,

    /// <summary>Windows process-container backend.</summary>
    [JsonStringEnumMemberName("processcontainer")]
    ProcessContainer,

    /// <summary>Windows Sandbox backend.</summary>
    [JsonStringEnumMemberName("windows_sandbox")]
    WindowsSandbox,

    /// <summary>WSL-based container backend.</summary>
    [JsonStringEnumMemberName("wslc")]
    Wslc,

    /// <summary>Linux LXC container backend.</summary>
    [JsonStringEnumMemberName("lxc")]
    Lxc,

    /// <summary>Hyperlight micro-VM backend.</summary>
    [JsonStringEnumMemberName("hyperlight")]
    Hyperlight,

    /// <summary>macOS Seatbelt backend.</summary>
    [JsonStringEnumMemberName("seatbelt")]
    Seatbelt,

    /// <summary>Cloud-hosted isolation session backend.</summary>
    [JsonStringEnumMemberName("isolation_session")]
    IsolationSession,

    /// <summary>Linux Bubblewrap namespace backend.</summary>
    [JsonStringEnumMemberName("bubblewrap")]
    Bubblewrap
}
