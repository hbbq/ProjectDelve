using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

// Retain the exact legacy Cell shape, while typed Cells need no placeholder enum.
public sealed class CellDefinitionJsonConverter : JsonConverter<CellDefinition>
{
    public override CellDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        string Name(string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(Name(nameof(CellDefinition.Position)), out var position))
            throw new JsonException("A Cell requires a Position.");
        var hasLegacy = root.TryGetProperty(Name(nameof(CellDefinition.Terrain)), out var legacy);
        var hasType = root.TryGetProperty(Name(nameof(CellDefinition.TerrainTypeId)), out var type);
        if (hasType && type.ValueKind != JsonValueKind.String)
            throw new JsonException("Terrain Type ID must be a string.");
        var typeId = hasType ? type.GetString() : null;
        if (hasType && string.IsNullOrWhiteSpace(typeId) || !hasLegacy && !hasType)
            throw new JsonException("A Cell requires a Terrain or a nonblank Terrain Type ID.");
        var kind = hasLegacy ? legacy.Deserialize<TerrainKind>(options) : TerrainKind.StoneFloor;
        var direction = root.TryGetProperty(Name(nameof(CellDefinition.Direction)), out var directionValue)
            ? directionValue.Deserialize<RailDirection?>(options) : null;
        var coal = root.TryGetProperty(Name(nameof(CellDefinition.CoalGeneration)), out var coalValue)
            ? coalValue.Deserialize<bool?>(options) : null;
        if (hasLegacy && hasType &&
            TerrainCompatibility.ResolveTypeId(kind, null, coal) != TerrainCompatibility.ResolveTypeId(kind, typeId, coal))
            throw new JsonException("A Cell cannot reference conflicting legacy and typed Terrain.");
        return new(position.Deserialize<Cell>(options) ?? throw new JsonException("Cell Position cannot be null."), kind, direction)
        {
            TerrainTypeId = typeId,
            CoalGeneration = coal
        };
    }

    public override void Write(Utf8JsonWriter writer, CellDefinition value, JsonSerializerOptions options)
    {
        string Name(string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        void Write<T>(string name, T item)
        {
            writer.WritePropertyName(Name(name));
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteStartObject();
        Write(nameof(value.Position), value.Position);
        if (value.TerrainTypeId is { } typeId) Write(nameof(value.TerrainTypeId), typeId);
        else Write(nameof(value.Terrain), value.Terrain);
        if (value.Direction is not null || options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
            Write(nameof(value.Direction), value.Direction);
        if (value.CoalGeneration is not null || options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
            Write(nameof(value.CoalGeneration), value.CoalGeneration);
        writer.WriteEndObject();
    }
}
