// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Custom JSON converter for <see cref="ContainerConfig"/> that writes properties
/// in the exact insertion order used by the TS SDK's <c>createConfigFromPolicy</c>.
/// This ensures byte-identical JSON output to the TypeScript reference implementation.
///
/// The TS code builds a ContainerConfig by:
/// 1. Object literal: version, containerId, lifecycle, process
/// 2. config.filesystem = ...
/// 3. config.ui = ...
/// 4. config.network = ...
/// 5. Backend-specific: containment, then backend block (experimental/lxc/processContainer)
///
/// For microvm (which skips steps 2–4): version, containerId, lifecycle, process, containment, filesystem
/// </summary>
internal sealed class ContainerConfigConverter : JsonConverter<ContainerConfig>
{
    public override ContainerConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Delegate deserialization to default behavior via a temporary options without this converter
        var tempOptions = new JsonSerializerOptions(options);
        tempOptions.Converters.Remove(this);
        // Use a secondary approach: read into JsonDocument then deserialize
        using var doc = JsonDocument.ParseValue(ref reader);
        return DeserializeFromElement(doc.RootElement, tempOptions);
    }

    private static ContainerConfig DeserializeFromElement(JsonElement element, JsonSerializerOptions options)
    {
        string version = element.GetProperty("version").GetString()!;
        string? containerId = element.TryGetProperty("containerId", out var cid) ? cid.GetString() : null;
        ContainmentValue? containment = null;
        if (element.TryGetProperty("containment", out var cont))
        {
            var contStr = cont.GetString();
            if (contStr is not null)
                containment = ContainmentValue.FromString(contStr);
        }

        LifecycleConfig? lifecycle = element.TryGetProperty("lifecycle", out var lc)
            ? JsonSerializer.Deserialize<LifecycleConfig>(lc.GetRawText(), options)
            : null;
        ProcessConfig? process = element.TryGetProperty("process", out var pc)
            ? JsonSerializer.Deserialize<ProcessConfig>(pc.GetRawText(), options)
            : null;
        ProcessContainerConfig? processContainer = element.TryGetProperty("processContainer", out var pcc)
            ? JsonSerializer.Deserialize<ProcessContainerConfig>(pcc.GetRawText(), options)
            : null;
#pragma warning disable CS0618 // Obsolete
        ProcessContainerConfig? appContainer = element.TryGetProperty("appContainer", out var ac)
            ? JsonSerializer.Deserialize<ProcessContainerConfig>(ac.GetRawText(), options)
            : null;
#pragma warning restore CS0618
        LxcConfig? lxc = element.TryGetProperty("lxc", out var lxcEl)
            ? JsonSerializer.Deserialize<LxcConfig>(lxcEl.GetRawText(), options)
            : null;
        FilesystemConfig? filesystem = element.TryGetProperty("filesystem", out var fs)
            ? JsonSerializer.Deserialize<FilesystemConfig>(fs.GetRawText(), options)
            : null;
        NetworkConfig? network = element.TryGetProperty("network", out var net)
            ? JsonSerializer.Deserialize<NetworkConfig>(net.GetRawText(), options)
            : null;
        ExperimentalConfig? experimental = element.TryGetProperty("experimental", out var exp)
            ? JsonSerializer.Deserialize<ExperimentalConfig>(exp.GetRawText(), options)
            : null;
        UiConfig? ui = element.TryGetProperty("ui", out var uiEl)
            ? JsonSerializer.Deserialize<UiConfig>(uiEl.GetRawText(), options)
            : null;

#pragma warning disable CS0618
        return new ContainerConfig
        {
            Version = version,
            ContainerId = containerId,
            Containment = containment,
            Lifecycle = lifecycle,
            Process = process,
            ProcessContainer = processContainer,
            AppContainer = appContainer,
            Lxc = lxc,
            Filesystem = filesystem,
            Network = network,
            Experimental = experimental,
            Ui = ui,
        };
#pragma warning restore CS0618
    }

    public override void Write(Utf8JsonWriter writer, ContainerConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // 1. Always: version, containerId, lifecycle, process (the initial object literal in TS)
        writer.WriteString("version", value.Version);

        if (value.ContainerId is not null)
            writer.WriteString("containerId", value.ContainerId);

        // Determine if this is a microvm config (microvm has containment before filesystem)
        bool isMicrovm = value.Containment?.Value == "microvm";

        if (value.Lifecycle is not null)
            WriteLifecycle(writer, value.Lifecycle);

        if (value.Process is not null)
            WriteProcess(writer, value.Process, options);

        if (isMicrovm)
        {
            // Microvm order: version, containerId, lifecycle, process, containment, filesystem
            WriteContainment(writer, value.Containment);
            if (value.Filesystem is not null)
                WriteFilesystem(writer, value.Filesystem);
        }
        else
        {
            // Non-microvm order: ..., filesystem, ui, network, containment, backend-block
            if (value.Filesystem is not null)
                WriteFilesystem(writer, value.Filesystem);

            if (value.Ui is not null)
                WriteUi(writer, value.Ui, options);

            if (value.Network is not null)
                WriteNetwork(writer, value.Network, options);

            WriteContainment(writer, value.Containment);

            // Backend-specific blocks (only one will be non-null)
            if (value.Experimental is not null)
                WriteExperimental(writer, value.Experimental, options);

            if (value.Lxc is not null)
                WriteLxc(writer, value.Lxc);

            if (value.ProcessContainer is not null)
                WriteProcessContainer(writer, value.ProcessContainer, options);

#pragma warning disable CS0618
            if (value.AppContainer is not null)
                WriteAppContainer(writer, value.AppContainer, options);
#pragma warning restore CS0618
        }

        writer.WriteEndObject();
    }

    private static void WriteContainment(Utf8JsonWriter writer, ContainmentValue? containment)
    {
        if (containment is not null)
            writer.WriteString("containment", containment.Value.Value);
    }

    private static void WriteLifecycle(Utf8JsonWriter writer, LifecycleConfig lifecycle)
    {
        writer.WritePropertyName("lifecycle");
        writer.WriteStartObject();
        if (lifecycle.DestroyOnExit is not null)
            writer.WriteBoolean("destroyOnExit", lifecycle.DestroyOnExit.Value);
        if (lifecycle.PreservePolicy is not null)
            writer.WriteBoolean("preservePolicy", lifecycle.PreservePolicy.Value);
        writer.WriteEndObject();
    }

    private static void WriteProcess(Utf8JsonWriter writer, ProcessConfig process, JsonSerializerOptions options)
    {
        writer.WritePropertyName("process");
        writer.WriteStartObject();
        writer.WriteString("commandLine", process.CommandLine);
        if (process.Cwd is not null)
            writer.WriteString("cwd", process.Cwd);
        if (process.Env is not null)
        {
            writer.WritePropertyName("env");
            JsonSerializer.Serialize(writer, process.Env, options);
        }
        if (process.Timeout is not null)
            writer.WriteNumber("timeout", process.Timeout.Value);
        writer.WriteEndObject();
    }

    private static void WriteFilesystem(Utf8JsonWriter writer, FilesystemConfig filesystem)
    {
        writer.WritePropertyName("filesystem");
        writer.WriteStartObject();
        if (filesystem.ReadwritePaths is not null)
        {
            writer.WritePropertyName("readwritePaths");
            WriteStringArray(writer, filesystem.ReadwritePaths);
        }
        if (filesystem.ReadonlyPaths is not null)
        {
            writer.WritePropertyName("readonlyPaths");
            WriteStringArray(writer, filesystem.ReadonlyPaths);
        }
        if (filesystem.DeniedPaths is not null)
        {
            writer.WritePropertyName("deniedPaths");
            WriteStringArray(writer, filesystem.DeniedPaths);
        }
        if (filesystem.ClearPolicyOnExit is not null)
            writer.WriteBoolean("clearPolicyOnExit", filesystem.ClearPolicyOnExit.Value);
        writer.WriteEndObject();
    }

    private static void WriteUi(Utf8JsonWriter writer, UiConfig ui, JsonSerializerOptions options)
    {
        writer.WritePropertyName("ui");
        writer.WriteStartObject();
        writer.WriteBoolean("disable", ui.Disable);
        writer.WriteString("clipboard", JsonSerializer.Serialize(ui.Clipboard, options).Trim('"'));
        writer.WriteBoolean("injection", ui.Injection);
        writer.WriteEndObject();
    }

    private static void WriteNetwork(Utf8JsonWriter writer, NetworkConfig network, JsonSerializerOptions options)
    {
        writer.WritePropertyName("network");
        writer.WriteStartObject();
        // TS creates network object with: defaultPolicy, allowLocalNetwork, allowedHosts,
        // blockedHosts, proxy — then enforcementMode is set afterward by backend builders.
        if (network.DefaultPolicy is not null)
            writer.WriteString("defaultPolicy", JsonSerializer.Serialize(network.DefaultPolicy, options).Trim('"'));
        if (network.AllowLocalNetwork is not null)
            writer.WriteBoolean("allowLocalNetwork", network.AllowLocalNetwork.Value);
        if (network.AllowedHosts is not null)
        {
            writer.WritePropertyName("allowedHosts");
            WriteStringArray(writer, network.AllowedHosts);
        }
        if (network.BlockedHosts is not null)
        {
            writer.WritePropertyName("blockedHosts");
            WriteStringArray(writer, network.BlockedHosts);
        }
        if (network.Proxy is not null)
        {
            writer.WritePropertyName("proxy");
            JsonSerializer.Serialize(writer, network.Proxy, options);
        }
        if (network.EnforcementMode is not null)
            writer.WriteString("enforcementMode", JsonSerializer.Serialize(network.EnforcementMode, options).Trim('"'));
#pragma warning disable CS0618
        if (network.RemoveRulesOnExit is not null)
            writer.WriteBoolean("removeRulesOnExit", network.RemoveRulesOnExit.Value);
#pragma warning restore CS0618
        writer.WriteEndObject();
    }

    private static void WriteExperimental(Utf8JsonWriter writer, ExperimentalConfig experimental, JsonSerializerOptions options)
    {
        writer.WritePropertyName("experimental");
        writer.WriteStartObject();
        if (experimental.Wslc is not null)
        {
            writer.WritePropertyName("wslc");
            JsonSerializer.Serialize(writer, experimental.Wslc, options);
        }
        if (experimental.Seatbelt is not null)
        {
            writer.WritePropertyName("seatbelt");
            writer.WriteStartObject();
            // Write non-null properties
            if (experimental.Seatbelt.ProfileOverride is not null)
                writer.WriteString("profileOverride", experimental.Seatbelt.ProfileOverride);
            if (experimental.Seatbelt.NestedPty is not null)
                writer.WriteBoolean("nestedPty", experimental.Seatbelt.NestedPty.Value);
            if (experimental.Seatbelt.KeychainAccess is not null)
                writer.WriteBoolean("keychainAccess", experimental.Seatbelt.KeychainAccess.Value);
            if (experimental.Seatbelt.ExtraMachLookups is not null)
            {
                writer.WritePropertyName("extraMachLookups");
                WriteStringArray(writer, experimental.Seatbelt.ExtraMachLookups);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteLxc(Utf8JsonWriter writer, LxcConfig lxc)
    {
        writer.WritePropertyName("lxc");
        writer.WriteStartObject();
        if (lxc.ContainerName is not null)
            writer.WriteString("containerName", lxc.ContainerName);
        if (lxc.Distribution is not null)
            writer.WriteString("distribution", lxc.Distribution);
        if (lxc.Release is not null)
            writer.WriteString("release", lxc.Release);
        if (lxc.DestroyOnExit is not null)
            writer.WriteBoolean("destroyOnExit", lxc.DestroyOnExit.Value);
        writer.WriteEndObject();
    }

    private static void WriteProcessContainer(Utf8JsonWriter writer, ProcessContainerConfig pc, JsonSerializerOptions options)
    {
        writer.WritePropertyName("processContainer");
        WriteProcessContainerBody(writer, pc, options);
    }

    private static void WriteAppContainer(Utf8JsonWriter writer, ProcessContainerConfig pc, JsonSerializerOptions options)
    {
        writer.WritePropertyName("appContainer");
        WriteProcessContainerBody(writer, pc, options);
    }

    private static void WriteProcessContainerBody(Utf8JsonWriter writer, ProcessContainerConfig pc, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (pc.Name is not null)
            writer.WriteString("name", pc.Name);
        if (pc.LeastPrivilege is not null)
            writer.WriteBoolean("leastPrivilege", pc.LeastPrivilege.Value);
        if (pc.Capabilities is not null)
        {
            writer.WritePropertyName("capabilities");
            WriteStringArray(writer, pc.Capabilities);
        }
        if (pc.Ui is not null)
        {
            writer.WritePropertyName("ui");
            writer.WriteStartObject();
            writer.WriteString("isolation", JsonSerializer.Serialize(pc.Ui.Isolation, options).Trim('"'));
            writer.WriteBoolean("desktopSystemControl", pc.Ui.DesktopSystemControl);
            writer.WriteString("systemSettings", pc.Ui.SystemSettings);
            writer.WriteBoolean("ime", pc.Ui.Ime);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, IReadOnlyList<string> items)
    {
        writer.WriteStartArray();
        foreach (var item in items)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
