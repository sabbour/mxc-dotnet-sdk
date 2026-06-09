// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Sabbour.Mxc.Sdk.Errors;

/// <summary>
/// Typed exception thrown by the MXC SDK in response to a wire-format error envelope.
/// </summary>
public class MxcException : Exception
{
    /// <summary>
    /// The typed error code if the wire value maps to a known <see cref="ErrorCode"/> member; null otherwise.
    /// </summary>
    public ErrorCode? Code { get; }

    /// <summary>
    /// The raw wire-format error code string, always preserved regardless of whether it maps to a known enum value.
    /// </summary>
    public string RawCode { get; }

    /// <summary>Optional structured details from the wire envelope.</summary>
    public IReadOnlyDictionary<string, object>? Details { get; }

    /// <summary>
    /// Creates an MxcException from a known error code, message, and optional details.
    /// </summary>
    public MxcException(ErrorCode code, string message, IReadOnlyDictionary<string, object>? details = null)
        : base(message)
    {
        Code = code;
        RawCode = ErrorCodeToWireString(code);
        Details = details;
    }

    /// <summary>
    /// Creates an MxcException from a known error code, message, inner exception, and optional details.
    /// </summary>
    public MxcException(ErrorCode code, string message, Exception innerException, IReadOnlyDictionary<string, object>? details = null)
        : base(message, innerException)
    {
        Code = code;
        RawCode = ErrorCodeToWireString(code);
        Details = details;
    }

    private MxcException(ErrorCode? code, string rawCode, string message, IReadOnlyDictionary<string, object>? details)
        : base(message)
    {
        Code = code;
        RawCode = rawCode;
        Details = details;
    }

    /// <summary>
    /// Constructs an MxcException from a wire-format error code string (snake_case).
    /// Unknown codes are preserved in <see cref="RawCode"/>; no exception is thrown for unrecognized values.
    /// </summary>
    public static MxcException FromCode(string code, string message, IReadOnlyDictionary<string, object>? details = null)
    {
        ErrorCode? typed = TryParseErrorCode(code);
        return new MxcException(typed, code, message, details);
    }

    private static ErrorCode? TryParseErrorCode(string code)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorCode>($"\"{code}\"");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ErrorCodeToWireString(ErrorCode code)
    {
        // Serialize strips quotes: "\"backend_error\"" → backend_error
        var json = JsonSerializer.Serialize(code);
        return json.Length >= 2 ? json[1..^1] : json;
    }
}
