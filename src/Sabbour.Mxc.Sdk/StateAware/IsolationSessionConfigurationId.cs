// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// IsoSession size profile. Unknown values are warned and downgraded to 'composable' on the Rust side.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IsolationSessionConfigurationId>))]
public enum IsolationSessionConfigurationId
{
    /// <summary>Small VM profile — minimal resources.</summary>
    [JsonStringEnumMemberName("small")]
    Small,

    /// <summary>Medium VM profile — balanced resources.</summary>
    [JsonStringEnumMemberName("medium")]
    Medium,

    /// <summary>Large VM profile — maximum resources.</summary>
    [JsonStringEnumMemberName("large")]
    Large,

    /// <summary>Composable profile — custom resource configuration.</summary>
    [JsonStringEnumMemberName("composable")]
    Composable
}
