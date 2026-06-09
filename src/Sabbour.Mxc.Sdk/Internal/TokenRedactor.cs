// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Sabbour.Mxc.Sdk.Internal;

/// <summary>
/// Shared utility for redacting wamToken values and capping string length
/// before they flow into exceptions or log/diagnostic paths.
/// </summary>
internal static partial class TokenRedactor
{
    /// <summary>Maximum length for messages flowing into exceptions.</summary>
    internal const int MaxExceptionMessageLength = 1024;

    /// <summary>Redaction placeholder.</summary>
    private const string RedactedPlaceholder = "<redacted>";

    /// <summary>
    /// Matches "wamToken":"&lt;value&gt;" in JSON-shaped text (the value is any non-quote run).
    /// </summary>
    private static readonly Regex WamTokenJsonPattern = CreateWamTokenJsonRegex();

    [GeneratedRegex(@"""wamToken""\s*:\s*""[^""]*""")]
    private static partial Regex CreateWamTokenJsonRegex();

    /// <summary>
    /// Redacts wamToken values and caps the string to <paramref name="maxLength"/>.
    /// </summary>
    internal static string RedactAndCap(string? input, int maxLength = MaxExceptionMessageLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var redacted = WamTokenJsonPattern.Replace(input, @"""wamToken"":""<redacted>""");

        if (maxLength > 0 && redacted.Length > maxLength)
        {
            return redacted[..maxLength] + " [truncated]";
        }

        return redacted;
    }

    /// <summary>
    /// Redacts wamToken values without capping length. Suitable for log-line redaction hooks.
    /// </summary>
    internal static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return WamTokenJsonPattern.Replace(input, @"""wamToken"":""<redacted>""");
    }
}
