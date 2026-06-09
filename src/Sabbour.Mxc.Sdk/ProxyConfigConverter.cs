// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Custom JSON converter for <see cref="ProxyConfig"/> that enforces one-of wire serialization.
/// Emits exactly one of: { "builtinTestServer": true }, { "localhost": N }, or { "url": "..." }.
/// </summary>
internal sealed class ProxyConfigConverter : JsonConverter<ProxyConfig>
{
    public override ProxyConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object for ProxyConfig.");

        ProxyConfig? result = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name in ProxyConfig.");

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "builtinTestServer":
                    reader.GetBoolean(); // consume value
                    result = ProxyConfig.BuiltinTestServer();
                    break;
                case "localhost":
                    var port = reader.GetInt32();
                    result = ProxyConfig.Localhost(port);
                    break;
                case "url":
                    var url = reader.GetString() ?? throw new JsonException("ProxyConfig url cannot be null.");
                    result = ProxyConfig.Url(url);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return result ?? throw new JsonException("ProxyConfig must contain exactly one variant.");
    }

    public override void Write(Utf8JsonWriter writer, ProxyConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ProxyConfig.BuiltinTestServerProxy:
                writer.WriteBoolean("builtinTestServer", true);
                break;
            case ProxyConfig.LocalhostProxy lp:
                writer.WriteNumber("localhost", lp.Port);
                break;
            case ProxyConfig.UrlProxy up:
                writer.WriteString("url", up.ProxyUrl);
                break;
            default:
                throw new JsonException("Unknown ProxyConfig variant.");
        }
        writer.WriteEndObject();
    }
}
