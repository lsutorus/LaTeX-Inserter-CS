using System.Text.Json;
using System.Text.Json.Serialization;
using SharpHook.Data;

namespace LaTeXInserter.Models;

/// <summary>
/// AOT-compatible converter for SharpHook's KeyCode enum.
/// Uses UnsafeJsonConstructor to avoid reflection on read.
/// </summary>
internal sealed class KeyCodeConverter : JsonConverter<KeyCode>
{
    public override KeyCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString()!;
            if (Enum.TryParse(str, out KeyCode code))
                return code;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return (KeyCode)reader.GetInt32();
        }

        return KeyCode.VcUndefined;
    }

    public override void Write(Utf8JsonWriter writer, KeyCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
