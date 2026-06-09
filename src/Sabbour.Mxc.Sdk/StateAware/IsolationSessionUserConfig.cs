// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk.StateAware;

/// <summary>
/// Entra credentials for an IsolationSession sandbox. wamToken is treated as a secret:
/// ToString() redacts it, but JSON serialization carries the token verbatim on the wire.
/// </summary>
public sealed class IsolationSessionUserConfig
{
    /// <summary>User principal name (Entra UPN) for the session.</summary>
    [JsonPropertyName("upn")]
    public string Upn { get; }

    /// <summary>
    /// WAM bearer token for Entra authentication. Redacted in ToString() and logs,
    /// but emitted verbatim in JSON serialization for the wire protocol.
    /// </summary>
    [JsonPropertyName("wamToken")]
    public string WamToken { get; }

    /// <summary>
    /// Creates user config from UPN and WAM token.
    /// </summary>
    [JsonConstructor]
    public IsolationSessionUserConfig(string upn, string wamToken)
    {
        Upn = upn ?? throw new ArgumentNullException(nameof(upn));
        WamToken = wamToken ?? throw new ArgumentNullException(nameof(wamToken));
    }

    /// <summary>
    /// Returns a string representation that redacts the wamToken for safe logging.
    /// ADVISORY: UPN is still emitted here (PII). Callers in log paths should be
    /// aware that UPN will appear in diagnostic output.
    /// </summary>
    public override string ToString() =>
        $"IsolationSessionUserConfig {{ upn: '{Upn}', wamToken: '<redacted>' }}";
}
