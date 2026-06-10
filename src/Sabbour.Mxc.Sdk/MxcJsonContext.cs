// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Sabbour.Mxc.Sdk.Policy;
using Sabbour.Mxc.Sdk.StateAware;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Source-generated JSON serializer context for all wire-format DTOs.
/// Ensures AOT compatibility and validates all types are serializable at build time.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(ContainerConfig))]
[JsonSerializable(typeof(SandboxPolicy))]
[JsonSerializable(typeof(PlatformSupport))]
[JsonSerializable(typeof(ProcessConfig))]
[JsonSerializable(typeof(LifecycleConfig))]
[JsonSerializable(typeof(ProcessContainerConfig))]
[JsonSerializable(typeof(BaseProcessUiConfig))]
[JsonSerializable(typeof(FilesystemConfig))]
[JsonSerializable(typeof(NetworkConfig))]
[JsonSerializable(typeof(ExperimentalConfig))]
[JsonSerializable(typeof(UiConfig))]
[JsonSerializable(typeof(WslcConfig))]
[JsonSerializable(typeof(PortMapping))]
[JsonSerializable(typeof(LxcConfig))]
[JsonSerializable(typeof(SeatbeltConfig))]
[JsonSerializable(typeof(WindowsSandboxConfig))]
[JsonSerializable(typeof(UiCapabilitySupport))]
[JsonSerializable(typeof(FilesystemPolicy))]
[JsonSerializable(typeof(NetworkPolicy))]
[JsonSerializable(typeof(UiPolicy))]
[JsonSerializable(typeof(ProxyConfig))]
[JsonSerializable(typeof(ContainmentValue))]
[JsonSerializable(typeof(Policy.FilesystemPolicyResult))]
[JsonSerializable(typeof(Policy.ToolsPolicyOptions))]
[JsonSerializable(typeof(IsolationSessionProvisionConfig))]
[JsonSerializable(typeof(IsolationSessionStartConfig))]
[JsonSerializable(typeof(IsolationSessionExecConfig))]
[JsonSerializable(typeof(IsolationSessionStopConfig))]
[JsonSerializable(typeof(IsolationSessionDeprovisionConfig))]
[JsonSerializable(typeof(IsolationSessionProvisionMetadata))]
[JsonSerializable(typeof(IsolationSessionUserConfig))]
[JsonSerializable(typeof(ExecResult))]
public partial class MxcJsonContext : JsonSerializerContext
{
}
