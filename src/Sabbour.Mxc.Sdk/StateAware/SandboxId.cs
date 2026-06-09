// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Sabbour.Mxc.Sdk.Errors;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Branded sandbox identifier returned by provision and threaded into subsequent phases.
/// The generic parameter prevents cross-backend mixing at compile time.
/// The runtime value is the plain wire string (e.g. "iso:abc123").
/// </summary>
/// <typeparam name="TBackend">The backend marker type this id belongs to.</typeparam>
public readonly record struct SandboxId<TBackend> where TBackend : IStateAwareBackend
{
    /// <summary>The raw wire-format sandbox id string.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a SandboxId from a wire string. Validates the prefix matches the backend.
    /// </summary>
    /// <exception cref="MxcException">Thrown when the id is null/empty or has a wrong prefix.</exception>
    public SandboxId(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw MxcException.FromCode("malformed_id", "sandboxId must not be null or empty");
        }

        var colonIdx = value.IndexOf(':');
        if (colonIdx < 0)
        {
            throw MxcException.FromCode("malformed_id", $"sandboxId must carry a backend prefix: {value}");
        }

        var prefix = value[..colonIdx];
        if (!string.Equals(prefix, TBackend.SandboxIdPrefix, StringComparison.Ordinal))
        {
            throw MxcException.FromCode(
                "malformed_id",
                $"sandboxId prefix '{prefix}' does not match expected '{TBackend.SandboxIdPrefix}' for backend '{TBackend.WireName}'");
        }

        Value = value;
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
