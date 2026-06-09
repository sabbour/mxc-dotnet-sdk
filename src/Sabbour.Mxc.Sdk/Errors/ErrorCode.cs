// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk.Errors;

/// <summary>
/// Closed set of MXC wire-format error codes. Mirrors MxcErrorCode on the
/// Rust side and serialises as the same snake_case strings on the wire.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorCode>))]
public enum ErrorCode
{
    /// <summary>The request payload could not be parsed or is structurally invalid.</summary>
    [JsonStringEnumMemberName("malformed_request")]
    MalformedRequest,

    /// <summary>The requested containment type/backend is not supported on this host.</summary>
    [JsonStringEnumMemberName("unsupported_containment")]
    UnsupportedContainment,

    /// <summary>The requested lifecycle phase is not valid for the current session state.</summary>
    [JsonStringEnumMemberName("unsupported_phase")]
    UnsupportedPhase,

    /// <summary>The requested containment backend is not available (missing binary or OS support).</summary>
    [JsonStringEnumMemberName("backend_unavailable")]
    BackendUnavailable,

    /// <summary>The session/container ID is syntactically invalid.</summary>
    [JsonStringEnumMemberName("malformed_id")]
    MalformedId,

    /// <summary>The session/container ID refers to a session that has expired or been recycled.</summary>
    [JsonStringEnumMemberName("stale_id")]
    StaleId,

    /// <summary>The session has not been provisioned yet.</summary>
    [JsonStringEnumMemberName("not_provisioned")]
    NotProvisioned,

    /// <summary>The session has not been started yet.</summary>
    [JsonStringEnumMemberName("not_started")]
    NotStarted,

    /// <summary>The session is already in the started state.</summary>
    [JsonStringEnumMemberName("already_started")]
    AlreadyStarted,

    /// <summary>The session is already stopped.</summary>
    [JsonStringEnumMemberName("already_stopped")]
    AlreadyStopped,

    /// <summary>The supplied policy failed validation against backend constraints.</summary>
    [JsonStringEnumMemberName("policy_validation")]
    PolicyValidation,

    /// <summary>An internal error occurred in the containment backend.</summary>
    [JsonStringEnumMemberName("backend_error")]
    BackendError
}
