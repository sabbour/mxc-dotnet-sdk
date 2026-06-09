// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// A validated containment wire value. Accepts ContainmentType names, ContainmentBackend names,
/// and LegacyContainmentAliases. Rejects unknown values at construction time.
/// Serializes as the plain wire string.
/// </summary>
[JsonConverter(typeof(ContainmentValueConverter))]
public readonly struct ContainmentValue : IEquatable<ContainmentValue>
{
    /// <summary>The canonical wire string for this containment value.</summary>
    public string Value { get; }

    private ContainmentValue(string value)
    {
        Value = value;
    }

    private static readonly HashSet<string> s_validValues = BuildValidSet();

    private static HashSet<string> BuildValidSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        // ContainmentType wire strings
        foreach (var ct in Enum.GetValues<ContainmentType>())
        {
            set.Add(JsonSerializer.Serialize(ct).Trim('"'));
        }

        // ContainmentBackend wire strings
        foreach (var cb in Enum.GetValues<ContainmentBackend>())
        {
            set.Add(JsonSerializer.Serialize(cb).Trim('"'));
        }

        // Legacy aliases
        foreach (var alias in LegacyContainmentAliases.Map.Keys)
        {
            set.Add(alias);
        }

        return set;
    }

    /// <summary>
    /// Creates a <see cref="ContainmentValue"/> from a wire string.
    /// Throws <see cref="ArgumentException"/> if the value is not a known ContainmentType,
    /// ContainmentBackend, or legacy alias.
    /// </summary>
    public static ContainmentValue FromString(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Containment value cannot be null or empty.", nameof(value));

        if (!s_validValues.Contains(value))
            throw new ArgumentException($"Unknown containment value: '{value}'.", nameof(value));

        return new ContainmentValue(value);
    }

    /// <summary>
    /// Tries to create a <see cref="ContainmentValue"/>. Returns false for unknown values.
    /// </summary>
    public static bool TryFromString(string value, out ContainmentValue result)
    {
        if (!string.IsNullOrEmpty(value) && s_validValues.Contains(value))
        {
            result = new ContainmentValue(value);
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>Returns the canonical wire string.</summary>
    public override string ToString() => Value;
    /// <summary>Ordinal equality comparison against another <see cref="ContainmentValue"/>.</summary>
    public bool Equals(ContainmentValue other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContainmentValue cv && Equals(cv);
    /// <inheritdoc/>
    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    /// <summary>Equality operator.</summary>
    public static bool operator ==(ContainmentValue left, ContainmentValue right) => left.Equals(right);
    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ContainmentValue left, ContainmentValue right) => !left.Equals(right);
}

/// <summary>
/// JSON converter for <see cref="ContainmentValue"/> — serializes/deserializes as a plain wire string.
/// </summary>
internal sealed class ContainmentValueConverter : JsonConverter<ContainmentValue>
{
    public override ContainmentValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString() ?? throw new JsonException("ContainmentValue cannot be null.");
        return ContainmentValue.FromString(str);
    }

    public override void Write(Utf8JsonWriter writer, ContainmentValue value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
