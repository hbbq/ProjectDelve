using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

/// <summary>JSON for concrete initial scenario data, after authoring helpers have expanded.</summary>
public static class ScenarioDefinitionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string ToJson(ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return JsonSerializer.Serialize(scenario, Options);
    }

    /// <summary>
    /// Parses JSON, then validates through normal game creation without starting a round.
    /// JSON errors throw JsonException; scenario validity errors retain the creation boundary's exceptions.
    /// </summary>
    public static ScenarioDefinition FromJson(string json)
        => FromJson(json, null);

    // A host may reject expensive input before materialization, without changing engine rules.
    public static ScenarioDefinition FromJson(string json, Action<ScenarioDefinition>? beforeCreation)
    {
        ArgumentNullException.ThrowIfNull(json);
        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(json, Options)
            ?? throw new JsonException("Expected a ScenarioDefinition object, not null.");
        beforeCreation?.Invoke(scenario);
        _ = GameEngine.CreateGame(scenario);
        return scenario;
    }
}
