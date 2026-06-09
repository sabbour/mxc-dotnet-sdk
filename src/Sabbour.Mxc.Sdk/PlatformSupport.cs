// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Platform support information returned by the native probe.
/// </summary>
public sealed record PlatformSupport
{
    /// <summary>Whether WXC is supported on the current platform.</summary>
    [JsonPropertyName("isSupported")]
    public required bool IsSupported { get; init; }

    /// <summary>Reason why the platform is not supported (if applicable).</summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>Available sandboxing methods on this platform.</summary>
    [JsonPropertyName("availableMethods")]
    public required IReadOnlyList<ContainmentBackend> AvailableMethods { get; init; }

    /// <summary>Tier that would be selected for an empty policy on this system.</summary>
    [JsonPropertyName("isolationTier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IsolationTier? IsolationTier { get; init; }

    /// <summary>Tier degradation warnings.</summary>
    [JsonPropertyName("isolationWarnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? IsolationWarnings { get; init; }

    /// <summary>Host UI-restriction capabilities.</summary>
    [JsonPropertyName("uiCapabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiCapabilitySupport? UiCapabilities { get; init; }
}

/// <summary>
/// Host support for enforcing sandbox UI restrictions.
/// </summary>
public sealed record UiCapabilitySupport
{
    /// <summary>Whether the host can block reads from the clipboard.</summary>
    [JsonPropertyName("canBlockClipboardRead")]
    public required bool CanBlockClipboardRead { get; init; }

    /// <summary>Whether the host can block writes to the clipboard.</summary>
    [JsonPropertyName("canBlockClipboardWrite")]
    public required bool CanBlockClipboardWrite { get; init; }

    /// <summary>Whether the host can block synthetic keyboard/mouse input.</summary>
    [JsonPropertyName("canBlockInputInjection")]
    public required bool CanBlockInputInjection { get; init; }

    /// <summary>Whether the host can block input method / IME changes.</summary>
    [JsonPropertyName("canBlockInputMethodChanges")]
    public required bool CanBlockInputMethodChanges { get; init; }

    /// <summary>Whether the host can block access to external UI object handles.</summary>
    [JsonPropertyName("canBlockExternalUiObjects")]
    public required bool CanBlockExternalUiObjects { get; init; }

    /// <summary>Whether the host can block access to global UI namespaces.</summary>
    [JsonPropertyName("canBlockGlobalUiNamespace")]
    public required bool CanBlockGlobalUiNamespace { get; init; }

    /// <summary>Whether the host can block desktop switching.</summary>
    [JsonPropertyName("canBlockDesktopSwitching")]
    public required bool CanBlockDesktopSwitching { get; init; }

    /// <summary>Whether the host can block logoff or shutdown requests.</summary>
    [JsonPropertyName("canBlockLogoffOrShutdown")]
    public required bool CanBlockLogoffOrShutdown { get; init; }

    /// <summary>Whether the host can block system parameter changes.</summary>
    [JsonPropertyName("canBlockSystemParameterChanges")]
    public required bool CanBlockSystemParameterChanges { get; init; }

    /// <summary>Whether the host can block display settings changes.</summary>
    [JsonPropertyName("canBlockDisplaySettingsChanges")]
    public required bool CanBlockDisplaySettingsChanges { get; init; }
}
