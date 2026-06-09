// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>IsolationSession provision-phase configuration.</summary>
public sealed record IsolationSessionProvisionConfig
{
    /// <summary>Wire protocol version string.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    /// <summary>Filesystem configuration for the provisioned session.</summary>
    [JsonPropertyName("filesystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FilesystemConfig? Filesystem { get; init; }

    /// <summary>User credentials for the session (Entra identity).</summary>
    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IsolationSessionUserConfig? User { get; init; }
}

/// <summary>IsolationSession start-phase configuration.</summary>
public sealed record IsolationSessionStartConfig
{
    /// <summary>Wire protocol version string.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    /// <summary>Size profile identifier for the session VM.</summary>
    [JsonPropertyName("configurationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IsolationSessionConfigurationId? ConfigurationId { get; init; }

    /// <summary>User credentials for the session (Entra identity).</summary>
    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IsolationSessionUserConfig? User { get; init; }
}

/// <summary>IsolationSession exec-phase configuration.</summary>
public sealed record IsolationSessionExecConfig
{
    /// <summary>Wire protocol version string.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    /// <summary>Process launch configuration for the exec call.</summary>
    [JsonPropertyName("process")]
    public required ProcessConfig Process { get; init; }
}

/// <summary>IsolationSession stop-phase configuration.</summary>
public sealed record IsolationSessionStopConfig
{
    /// <summary>Wire protocol version string.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }
}

/// <summary>IsolationSession deprovision-phase configuration.</summary>
public sealed record IsolationSessionDeprovisionConfig
{
    /// <summary>Wire protocol version string.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }
}

/// <summary>
/// IsolationSession's provision-phase metadata: the per-instance agent user account name.
/// </summary>
public sealed record IsolationSessionProvisionMetadata
{
    /// <summary>The agent user account name provisioned for this session instance.</summary>
    [JsonPropertyName("agentUserName")]
    public required string AgentUserName { get; init; }
}

/// <summary>Placeholder for backends/phases that return no metadata.</summary>
public sealed record NoMetadata;
