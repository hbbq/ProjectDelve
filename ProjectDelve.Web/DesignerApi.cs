using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record DesignerUnitType(string UnitTypeId, string DisplayName, int MaxHp,
    IReadOnlyList<Cell> FootprintOffsets, int CellSpan, ResourceStorage? ResourceStorage)
{
    public UnitType? Definition { get; init; }
}
public sealed record DesignerMetadataRequest(List<UnitType> UnitTypes, List<UnitPlacement>? Units = null, List<string>? MachineIds = null,
    List<TerrainDefinition>? TerrainTypes = null, List<TerrainAssemblyDefinition>? TerrainAssemblies = null,
    List<FixedComponentDefinition>? FixedComponentTypes = null, List<LootCardDefinition>? LootCardDefinitions = null);
public sealed record DesignerError(string Message, string? Path);
public sealed record DesignerValidation(bool Valid, IReadOnlyList<DesignerError> Errors);
public sealed record DesignerImportRequest(string Transport);
public sealed record DesignerLegacyTerrainMapping(TerrainKind Kind, string TerrainTypeId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CoalGeneration = null);
public sealed class DesignerBoardLimitException() : ArgumentException(
    $"This host supports boards up to {DesignerApi.MaxBoardSize} × {DesignerApi.MaxBoardSize} (editor policy).");

// Stateless authoring operations. The engine still owns all scenario legality.
public static class DesignerApi
{
    public const int MaxBoardSize = 50; // Web/editor policy, never a Delve rule.

    private static DesignerUnitType Metadata(UnitType type)
    {
        var offsets = FootprintGeometry.OccupiedCells(type.Footprint, new(0, 0));
        return new(type.Id, type.DisplayName ?? type.Id, type.Hp, offsets,
            offsets.Max(cell => cell.X) + 1, type.ResourceStorage) { Definition = type };
    }

    private static Dictionary<string, string> ProductionDescriptions(IEnumerable<TerrainDefinition> definitions, IReadOnlyList<ResourceType>? resources = null) =>
        definitions.Where(type => type.Production is not null)
            .ToDictionary(type => type.Id, type => EnvironmentalProductionContent.Describe(type.Production, resources)!, StringComparer.Ordinal);

    public static void CheckHostLimits(ScenarioDefinition definition)
    {
        if (definition.Board is { } board && (board.Width > MaxBoardSize || board.Height > MaxBoardSize))
            throw new DesignerBoardLimitException();
    }

    public static void Map(WebApplication app)
    {
        var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
        app.MapGet("/api/designer/catalog", () => Results.Ok(new
        {
            maxBoardSize = MaxBoardSize,
            scenarios = PlaytestScenarios.Catalog.Select(s => new { s.Id, s.Name }),
            cellMarkerTypes = CellMarkerTypes.All,
            lootCards = LootCards.All,
            equipmentTypes = canonical.Equipment,
            itemTypes = canonical.Items,
            lootCardDefinitions = canonical.LootCards,
            worldCardDefinitions = canonical.WorldCards.Select(card => new
                { card.Id, card.Name, card.Lifecycle, card.Modifiers, card.Immediate, description = card.Description }),
            lootPresets = ResolvedLootPresets(canonical),
            defaultLootPreset = LootPresets.Default,
            machineTypes = MachineTypes.All,
            resourceTypes = canonical.Resources,
            unitTypes = canonical.Units.Select(Metadata).ToArray(),
            terrains = Enum.GetNames<TerrainKind>(),
            terrainDefinitions = canonical.Terrain,
            fixedComponentDefinitions = canonical.FixedComponents,
            productionDescriptions = ProductionDescriptions(canonical.Terrain, canonical.Resources),
            legacyTerrainMappings = Enum.GetValues<TerrainKind>()
                .Select(kind => new DesignerLegacyTerrainMapping(kind, TerrainCatalog.Legacy(kind).Id))
                .Append(new(TerrainKind.Lava, TerrainCatalog.Legacy(TerrainKind.Lava, false).Id, false)),
            legacyTerrainTypeAliases = new Dictionary<string, TerrainKind>
                { [TerrainCompatibility.LegacyLavaId] = TerrainKind.Lava },
            railDirections = Enum.GetNames<RailDirection>(),
            edgeKinds = Enum.GetNames<EdgeKind>(),
            postures = Enum.GetNames<Posture>(),
            controllers = Enum.GetNames<ControllerKind>(),
            orderKinds = Enum.GetNames<OrderKind>()
        }));
        // Content projection works even when the board or Objectives are incomplete.
        // Use the same content resolver as scenario creation; never materialize a game here.
        app.MapPost("/api/designer/metadata", (JsonElement content) => Execute(() =>
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
            var input = content.Deserialize<DesignerMetadataRequest>(options);
            var custom = input?.UnitTypes
                ?? throw new ArgumentException("Unit Types are required.");
            if (custom.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id)) || custom.Select(t => t.Id).Distinct().Count() != custom.Count)
                throw new ArgumentException("Custom Unit Types must have unique, nonblank IDs.");
            var effectiveUnits = UnitContent.Resolve(custom, canonical.Units);
            var terrainDefinitions = TerrainCatalog.Resolve(input!.TerrainTypes, input.TerrainAssemblies, input.FixedComponentTypes, canonical);
            return Results.Ok(new { unitTypes = effectiveUnits.Select(Metadata).ToArray(),
                terrainDefinitions,
                fixedComponentDefinitions = canonical.FixedComponents.Concat(input.FixedComponentTypes ?? []).ToArray(),
                productionDescriptions = ProductionDescriptions(terrainDefinitions, canonical.Resources),
                lootPresets = ResolvedLootPresets(canonical, input.LootCardDefinitions),
                unitIds = ScenarioIdentity.UnitIds(input!.Units ?? [], input.MachineIds ?? []) });
        }));
        app.MapGet("/api/designer/scenarios/{id}", (string id) =>
            PlaytestScenarios.Catalog.Any(s => s.Id == id)
                ? Results.Text(ScenarioDefinitionJson.ToJson(PlaytestScenarios.Definition(id)), "application/json")
                : Results.NotFound());
        app.MapPost("/api/designer/import", (DesignerImportRequest request) => Execute(() =>
        {
            var definition = ScenarioDefinitionTransport.Decode(request.Transport, CheckHostLimits, canonical);
            return Results.Text(ScenarioDefinitionJson.ToJson(definition), "application/json");
        }));
        // JsonElement preserves the scenario JSON contract: no independent HTTP enum/model parser.
        app.MapPost("/api/designer/validate", (JsonElement draft) => Execute(() =>
        {
            _ = ScenarioDefinitionJson.FromJson(draft.GetRawText(), CheckHostLimits, canonical);
            return Results.Ok(new DesignerValidation(true, []));
        }, validation: true));
        app.MapPost("/api/designer/export", (JsonElement draft) => Execute(() =>
        {
            var definition = ScenarioDefinitionJson.FromJson(draft.GetRawText(), CheckHostLimits, canonical);
            return Results.Ok(new { transport = ScenarioDefinitionTransport.Encode(definition) });
        }));
    }

    private static IEnumerable<LootPreset> ResolvedLootPresets(ContentBundle canonical, IEnumerable<LootCardDefinition>? local = null)
    {
        var effective = LootCardCatalog.Effective(local, canonical.LootCards);
        return canonical.LootPresets.Select(preset => preset with { Composition = LootPresets.Resolve(preset.Id, effective, canonical.LootPresets) });
    }

    private static IResult Execute(Func<IResult> operation, bool validation = false)
    {
        try { return operation(); }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
        {
            var path = ScenarioValidationContext.PathOf(error) ?? (error is DesignerBoardLimitException ? "board"
                : error is JsonException json ? json.Path : null);
            return Results.Json(new DesignerValidation(false, [new(error.Message, path)]),
                statusCode: validation ? 200 : 400);
        }
    }
}
