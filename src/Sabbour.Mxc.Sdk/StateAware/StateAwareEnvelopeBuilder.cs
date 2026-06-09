// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Builds the wire-format JSON envelope for state-aware lifecycle requests.
/// Faithful port of state-aware-helper.ts buildStateAwareEnvelope.
/// </summary>
internal static class StateAwareEnvelopeBuilder
{
    /// <summary>
    /// Schema version defaulted when the caller's config omits a version.
    /// Mirrors TS STATE_AWARE_VERSION constant.
    /// </summary>
    public const string StateAwareVersion = "0.6.0-alpha";

    /// <summary>
    /// Cross-cutting fields lifted to the envelope's top level, in exact TS order.
    /// Port of state-aware-helper.ts CROSS_CUTTING_FIELDS (line 15):
    ///   ['filesystem', 'network', 'ui', 'process']
    /// Uses a fixed array (not HashSet) to guarantee deterministic iteration order.
    /// </summary>
    private static readonly string[] CrossCuttingFields = ["filesystem", "network", "ui", "process"];

    /// <summary>
    /// Builds the envelope JSON string from a phase config DTO.
    /// </summary>
    /// <typeparam name="TConfig">The phase config type.</typeparam>
    /// <param name="phase">Lifecycle phase name.</param>
    /// <param name="backendWireName">Backend wire key (e.g. "isolation_session").</param>
    /// <param name="containment">Containment value (provision only).</param>
    /// <param name="sandboxId">Sandbox id wire string (non-provision phases).</param>
    /// <param name="config">Phase config DTO (may be null).</param>
    /// <param name="configTypeInfo">Source-gen JsonTypeInfo for the config.</param>
    /// <returns>Envelope JSON string ready for base64 encoding.</returns>
    internal static string BuildEnvelope<TConfig>(
        string phase,
        string backendWireName,
        string? containment,
        string? sandboxId,
        TConfig? config,
        JsonTypeInfo<TConfig> configTypeInfo)
    {
        // Serialize the config to a JsonObject so we can pick fields off it
        JsonObject? configObj = null;
        if (config is not null)
        {
            var configJson = JsonSerializer.Serialize(config, configTypeInfo);
            configObj = JsonNode.Parse(configJson)?.AsObject();
        }

        // Extract version, default if empty/null
        string version = StateAwareVersion;
        if (configObj is not null && configObj.TryGetPropertyValue("version", out var versionNode))
        {
            configObj.Remove("version");
            var versionStr = versionNode?.GetValue<string>();
            if (!string.IsNullOrEmpty(versionStr))
            {
                version = versionStr;
            }
        }

        // Build envelope with ordered properties
        var envelope = new JsonObject
        {
            ["version"] = version,
            ["phase"] = phase
        };

        if (containment is not null)
        {
            envelope["containment"] = containment;
        }

        if (sandboxId is not null)
        {
            envelope["sandboxId"] = sandboxId;
        }

        // Lift cross-cutting fields to top-level
        if (configObj is not null)
        {
            var keysToRemove = new List<string>();
            foreach (var field in CrossCuttingFields)
            {
                if (configObj.TryGetPropertyValue(field, out var fieldNode) && fieldNode is not null)
                {
                    envelope[field] = fieldNode.DeepClone();
                    keysToRemove.Add(field);
                }
            }
            foreach (var key in keysToRemove)
            {
                configObj.Remove(key);
            }
        }

        // Remaining config fields go under experimental.<backend>.<phase>
        if (configObj is not null && configObj.Count > 0)
        {
            var phaseObj = new JsonObject();
            foreach (var kvp in configObj)
            {
                phaseObj[kvp.Key] = kvp.Value?.DeepClone();
            }

            envelope["experimental"] = new JsonObject
            {
                [backendWireName] = new JsonObject
                {
                    [phase] = phaseObj
                }
            };
        }

        return envelope.ToJsonString(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });
    }
}
