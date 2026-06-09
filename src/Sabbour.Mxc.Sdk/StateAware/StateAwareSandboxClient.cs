// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Internal;
using Sabbour.Mxc.Sdk.Sandbox;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Generic state-aware sandbox client carrying per-phase config/metadata types
/// and their AOT-safe JsonTypeInfo references.
/// Callers use the concrete accessor (e.g. StateAwareSandboxes.IsolationSession)
/// and never spell these generic parameters.
/// </summary>
public sealed class StateAwareSandboxClient<
    TBackend,
    TProvisionConfig,
    TStartConfig,
    TExecConfig,
    TStopConfig,
    TDeprovisionConfig,
    TProvisionMetadata,
    TStartMetadata,
    TStopMetadata,
    TDeprovisionMetadata>
    where TBackend : IStateAwareBackend
{
    private readonly IStateAwareSpawnRunner _runner;
    private readonly JsonTypeInfo<TProvisionConfig> _provisionConfigTypeInfo;
    private readonly JsonTypeInfo<TStartConfig> _startConfigTypeInfo;
    private readonly JsonTypeInfo<TExecConfig> _execConfigTypeInfo;
    private readonly JsonTypeInfo<TStopConfig> _stopConfigTypeInfo;
    private readonly JsonTypeInfo<TDeprovisionConfig> _deprovisionConfigTypeInfo;
    private readonly JsonTypeInfo<TProvisionMetadata> _provisionMetadataTypeInfo;

    internal StateAwareSandboxClient(
        IStateAwareSpawnRunner runner,
        JsonTypeInfo<TProvisionConfig> provisionConfigTypeInfo,
        JsonTypeInfo<TStartConfig> startConfigTypeInfo,
        JsonTypeInfo<TExecConfig> execConfigTypeInfo,
        JsonTypeInfo<TStopConfig> stopConfigTypeInfo,
        JsonTypeInfo<TDeprovisionConfig> deprovisionConfigTypeInfo,
        JsonTypeInfo<TProvisionMetadata> provisionMetadataTypeInfo)
    {
        _runner = runner;
        _provisionConfigTypeInfo = provisionConfigTypeInfo;
        _startConfigTypeInfo = startConfigTypeInfo;
        _execConfigTypeInfo = execConfigTypeInfo;
        _stopConfigTypeInfo = stopConfigTypeInfo;
        _deprovisionConfigTypeInfo = deprovisionConfigTypeInfo;
        _provisionMetadataTypeInfo = provisionMetadataTypeInfo;
    }

    /// <summary>
    /// Provisions a state-aware sandbox. Returns a branded sandbox id and provision metadata.
    /// </summary>
    public async Task<ProvisionResult<TBackend, TProvisionMetadata>> ProvisionSandboxAsync(
        TProvisionConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Provision,
            TBackend.WireName,
            containment: TBackend.WireName,
            sandboxId: null,
            config,
            _provisionConfigTypeInfo);

        var result = await _runner.SpawnAndCollectAsync(envelope, opts, cancellationToken).ConfigureAwait(false);
        var parsed = ParseNonExecResponse(result.Stdout);

        // Extract sandboxId and metadata from the result object
        string? sandboxIdStr = null;
        TProvisionMetadata? metadata = default;

        if (parsed.TryGetProperty("sandboxId", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
        {
            sandboxIdStr = sidEl.GetString();
        }

        if (string.IsNullOrEmpty(sandboxIdStr))
        {
            throw new InvalidOperationException("Provision response did not contain a sandboxId");
        }

        if (parsed.TryGetProperty("metadata", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
        {
            metadata = JsonSerializer.Deserialize(metaEl.GetRawText(), _provisionMetadataTypeInfo);
        }

        return new ProvisionResult<TBackend, TProvisionMetadata>
        {
            SandboxId = new SandboxId<TBackend>(sandboxIdStr),
            Metadata = metadata,
        };
    }

    /// <summary>Starts a previously provisioned sandbox.</summary>
    public async Task<PhaseResult<TStartMetadata>> StartSandboxAsync(
        SandboxId<TBackend> sandboxId,
        TStartConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Start,
            TBackend.WireName,
            containment: null,
            sandboxId: sandboxId.Value,
            config,
            _startConfigTypeInfo);

        var result = await _runner.SpawnAndCollectAsync(envelope, opts, cancellationToken).ConfigureAwait(false);
        ParseNonExecResponse(result.Stdout); // validates {result}/{error}
        return new PhaseResult<TStartMetadata>();
    }

    /// <summary>
    /// Buffered exec — resolves with stdout/stderr/exitCode.
    /// Throws MxcException on dispatch failure (exit != 0 and stdout is an error envelope).
    /// </summary>
    public async Task<ExecResult> ExecSandboxAsync(
        SandboxId<TBackend> sandboxId,
        TExecConfig config,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Exec,
            TBackend.WireName,
            containment: null,
            sandboxId: sandboxId.Value,
            config,
            _execConfigTypeInfo);

        var result = await _runner.SpawnAndCollectAsync(envelope, opts, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            var error = SpawnHelper.TryParseErrorEnvelope(result.Stdout);
            if (error is not null)
            {
                throw error;
            }
        }

        return new ExecResult
        {
            Stdout = result.Stdout,
            Stderr = result.Stderr,
            ExitCode = result.ExitCode,
        };
    }

    /// <summary>
    /// Streaming exec — returns an IPtyConnection without parsing output.
    /// </summary>
    public IPtyConnection ExecSandbox(
        SandboxId<TBackend> sandboxId,
        TExecConfig config,
        SandboxSpawnOptions? options = null)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Exec,
            TBackend.WireName,
            containment: null,
            sandboxId: sandboxId.Value,
            config,
            _execConfigTypeInfo);

        return _runner.SpawnStreaming(envelope, opts);
    }

    /// <summary>Stops a started sandbox without releasing provision-side resources.</summary>
    public async Task<PhaseResult<TStopMetadata>> StopSandboxAsync(
        SandboxId<TBackend> sandboxId,
        TStopConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Stop,
            TBackend.WireName,
            containment: null,
            sandboxId: sandboxId.Value,
            config,
            _stopConfigTypeInfo);

        var result = await _runner.SpawnAndCollectAsync(envelope, opts, cancellationToken).ConfigureAwait(false);
        ParseNonExecResponse(result.Stdout);
        return new PhaseResult<TStopMetadata>();
    }

    /// <summary>Releases all backend resources for a provisioned sandbox.</summary>
    public async Task<PhaseResult<TDeprovisionMetadata>> DeprovisionSandboxAsync(
        SandboxId<TBackend> sandboxId,
        TDeprovisionConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new SandboxSpawnOptions();
        var envelope = StateAwareEnvelopeBuilder.BuildEnvelope(
            Phase.Deprovision,
            TBackend.WireName,
            containment: null,
            sandboxId: sandboxId.Value,
            config,
            _deprovisionConfigTypeInfo);

        var result = await _runner.SpawnAndCollectAsync(envelope, opts, cancellationToken).ConfigureAwait(false);
        ParseNonExecResponse(result.Stdout);
        return new PhaseResult<TDeprovisionMetadata>();
    }

    /// <summary>
    /// Parses a non-exec response envelope: {result: ...} or {error: ...}.
    /// Returns the JsonElement for the 'result' property on success.
    /// </summary>
    private static JsonElement ParseNonExecResponse(string stdout)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout.Trim());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse state-aware response envelope: {TokenRedactor.RedactAndCap(ex.Message)}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorEl))
            {
                var code = errorEl.TryGetProperty("code", out var codeEl) ? codeEl.GetString() ?? "unknown" : "unknown";
                var message = errorEl.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "" : "";
                // R1: Redact wamToken and cap error message/details flowing into exceptions
                message = TokenRedactor.RedactAndCap(message);
                IReadOnlyDictionary<string, object>? details = null;
                if (errorEl.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Object)
                {
                    details = ParseDetails(detailsEl);
                }
                throw MxcException.FromCode(code, message, details);
            }

            if (root.TryGetProperty("result", out var resultEl))
            {
                // Clone so we can dispose the document
                return resultEl.Clone();
            }

            throw new InvalidOperationException(
                $"Unexpected state-aware response envelope shape: {TokenRedactor.RedactAndCap(stdout)}");
        }
    }

    private static IReadOnlyDictionary<string, object>? ParseDetails(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = MaterializeValue(prop.Value);
        }
        return dict.Count > 0 ? dict : null;
    }

    private static object MaterializeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => MaterializeObject(value),
            JsonValueKind.Array => MaterializeArray(value),
            _ => value.GetRawText()
        };
    }

    private static Dictionary<string, object> MaterializeObject(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = MaterializeValue(prop.Value);
        }
        return dict;
    }

    private static List<object> MaterializeArray(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(MaterializeValue(item));
        }
        return list;
    }
}
