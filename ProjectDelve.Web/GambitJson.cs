using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// The Gambit controller contract is intentionally more compact than browser-facing JSON.
internal static class GambitJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    internal static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new GambitCellJsonConverter());
        return options;
    }
}

internal sealed class GambitCellJsonConverter : JsonConverter<Cell>
{
    public override Cell Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A Gambit coordinate must be [x,y].");
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var x))
            throw new JsonException("A Gambit coordinate must contain an integer x value.");
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var y))
            throw new JsonException("A Gambit coordinate must contain an integer y value.");
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("A Gambit coordinate must contain exactly two values.");
        return new(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Cell value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}