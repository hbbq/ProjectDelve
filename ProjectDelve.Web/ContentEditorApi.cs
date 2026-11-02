using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ContentEditorContext
{
    public List<UnitType> UnitTypes { get; init; } = [];
    public List<EquipmentDefinition> EquipmentTypes { get; init; } = [];
    public List<ItemDefinition> ItemTypes { get; init; } = [];
    public List<LootCardDefinition> LootCardDefinitions { get; init; } = [];
    public List<ScenarioContentReference>? ContentReferences { get; init; }
}
public sealed record ContentEditorRequest(string Family, JsonElement Definition,
    string? ExistingId = null, ContentEditorContext? Context = null);
public sealed record ContentEditorDiagnostic(string Path, string Code, string Message);
public sealed record ContentEditorResponse(bool Valid, IReadOnlyList<ContentEditorDiagnostic> Diagnostics)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentBrowserResponse? Catalog { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Definition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContentBrowserEntry? Entry { get; init; }
}

// Destination-independent authoring: catalogs supply dependencies, never identity,
// placement, ownership or a running game's data.
public static class ContentEditorApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static void Map(WebApplication app)
    {
        ContentBundle Canonical() => app.Services.GetRequiredService<CanonicalContent>().Bundle;
        app.MapPost("/api/content/validate", async (HttpRequest request) =>
        {
            try
            {
                var input = JsonSerializer.Deserialize<ContentEditorRequest>(await Read(request), Json)
                    ?? throw new JsonException("A content validation request is required.");
                return Results.Ok(Validate(input, Canonical()));
            }
            catch (JsonException error)
            {
                return Results.Json(Invalid(JsonDiagnostic(error)), statusCode: 400);
            }
        });
        app.MapPost("/api/content/context", async (HttpRequest request) =>
        {
            try
            {
                var context = JsonSerializer.Deserialize<ContentEditorContext>(await Read(request), Json)
                    ?? throw new JsonException("A dependency context is required.");
                var resolved = Resolve(context, contextPath: "", canonical: Canonical());
                return Results.Ok(ContentBrowserCatalog.Pinned(ContentBrowserCatalog.Definitions(resolved.Equipment, resolved.Items, resolved.Loot, resolved.Units, resolved.LocalUnitIds, Canonical()), context.ContentReferences, resolved.LocalUnitIds));
            }
            catch (Exception error) when (error is JsonException or ContentEditorException or ContentCompositionException)
            {
                var diagnostic = error is ContentEditorException content ? content.Diagnostic : error is JsonException json ? JsonDiagnostic(json)
                    : new ContentEditorDiagnostic("contentReferences", "validation", error.Message);
                return Results.Json(new ContentBrowserFailure(new("definitions", "Draft content"),
                    [new(diagnostic.Message, diagnostic.Path, diagnostic.Code)]), statusCode: 400);
            }
        });
    }

    public static ContentEditorResponse Validate(ContentEditorRequest request, ContentBundle? canonical = null)
    {
        canonical ??= ContentBundle.Canonical();
        if (request.Family is not ("equipment" or "lootCards"))
            return Invalid(new("family", "unsupportedFamily", "This editor supports Equipment and Loot Cards."));
        object candidate;
        try
        {
            if (request.Definition.ValueKind != JsonValueKind.Object)
                return Invalid(new("definition", "required", "A typed content definition object is required."));
            candidate = ParseDefinition(request);
        }
        catch (JsonException error) { return Invalid(JsonDiagnostic(error, "definition")); }
        try
        {
            var resolved = Resolve(request.Context ?? new(), candidate, request.ExistingId, canonical: canonical);
            var catalog = ContentBrowserCatalog.Pinned(ContentBrowserCatalog.Definitions(resolved.Equipment, resolved.Items, resolved.Loot, resolved.Units, resolved.LocalUnitIds, canonical), request.Context?.ContentReferences, resolved.LocalUnitIds);
            var id = candidate is EquipmentDefinition equipment ? equipment.Id : ((LootCardDefinition)candidate).Id;
            return new(true, [])
            {
                Definition = JsonSerializer.SerializeToElement(candidate, candidate.GetType(), Json),
                Entry = catalog.Entries.Single(entry => entry.Family == request.Family && entry.Id == id)
            };
        }
        catch (ContentEditorException error) { return Invalid(error.Diagnostic); }
        catch (ContentCompositionException error) { return Invalid(new("contentReferences", "validation", error.Message)); }
    }

    internal static object ParseDefinition(ContentEditorRequest request) => request.Family switch
    {
        "equipment" => request.Definition.Deserialize<EquipmentDefinition>(Json) ?? throw new JsonException("An Equipment definition is required."),
        "lootCards" => request.Definition.Deserialize<LootCardDefinition>(Json) ?? throw new JsonException("A Loot Card definition is required."),
        _ => throw new ArgumentException("This editor supports Equipment and Loot Cards.")
    };

    private sealed record ResolvedContext(List<EquipmentDefinition> Equipment, List<ItemDefinition> Items, List<LootCardDefinition> Loot,
        List<UnitType> Units, IReadOnlySet<string> LocalUnitIds);
    private sealed class ContentEditorException(ContentEditorDiagnostic diagnostic) : Exception(diagnostic.Message)
    { public ContentEditorDiagnostic Diagnostic => diagnostic; }

    private static ResolvedContext Resolve(ContentEditorContext context, object? candidate = null, string? existingId = null,
        string contextPath = "context", ContentBundle? canonical = null)
    {
        canonical ??= ContentBundle.Canonical();
        if (candidate is not null && canonical.Publications.Any(p => p.Identity == ContentDefinitions.Identity(candidate)))
            throw Error("definition.id", "published", "This definition has been published to Canonical. Create a copy with a new Type ID.");
        canonical = ScenarioContentReferences.Compose(canonical, context.ContentReferences).Definitions;
        canonical = CanonicalPublications.ForLocal(canonical, new ContentBundle { Units = [.. context.UnitTypes ?? []],
            Equipment = [.. context.EquipmentTypes ?? []], Items = [.. context.ItemTypes ?? []], LootCards = [.. context.LootCardDefinitions ?? []] });
        string Path(string field) => contextPath.Length == 0 ? field : contextPath + "." + field;
        if (context.UnitTypes is null) throw Error(Path("unitTypes"), "required", "Unit dependencies must be an array.");
        if (context.EquipmentTypes is null) throw Error(Path("equipmentTypes"), "required", "Equipment dependencies must be an array.");
        if (context.ItemTypes is null) throw Error(Path("itemTypes"), "required", "Item dependencies must be an array.");
        if (context.LootCardDefinitions is null) throw Error(Path("lootCardDefinitions"), "required", "Loot Card dependencies must be an array.");
        if (candidate is LootCardDefinition proposedLoot && canonical.LootCards.Any(type => type.Id == proposedLoot.Id))
            throw Error("definition.id", "canonicalId", "Canonical Loot Card IDs are reserved. Create a copy with a new ID to edit historical colliding content.");

        List<UnitType> units;
        var unitPaths = Enumerable.Range(0, context.UnitTypes.Count).ToDictionary(index => $"unitTypes[{index}]", index => $"{Path("unitTypes")}[{index}]");
        try { units = UnitContent.Resolve(context.UnitTypes, canonical.Units); }
        catch (ArgumentException error) { throw Rebase(error, unitPaths, contextPath); }

        var equipment = context.EquipmentTypes.ToList();
        var items = context.ItemTypes.ToList();
        var loot = context.LootCardDefinitions.ToList();
        var equipmentPaths = Enumerable.Range(0, equipment.Count).Select(index => $"{Path("equipmentTypes")}[{index}]").ToList();
        var itemPaths = Enumerable.Range(0, items.Count).Select(index => $"{Path("itemTypes")}[{index}]").ToList();
        var lootPaths = Enumerable.Range(0, loot.Count).Select(index => $"{Path("lootCardDefinitions")}[{index}]").ToList();
        if (candidate is EquipmentDefinition equipmentDefinition)
            Replace(equipment, equipmentPaths, equipmentDefinition, existingId, type => type.Id);
        else if (candidate is LootCardDefinition lootDefinition)
            Replace(loot, lootPaths, lootDefinition, existingId, type => type.Id);

        // Historical scenarios may already own a local Loot ID that later became
        // canonical. Validate those authored rows without assigning runtime aliases.
        // New/edited declarations above cannot introduce or edit such collisions.
        var localLootPaths = Enumerable.Range(0, lootPaths.Count).ToDictionary(index => $"lootCardDefinitions[{index}]", index => lootPaths[index]);
        try { LootCardCatalog.Validate(loot); }
        catch (ArgumentException error) { throw Rebase(error, localLootPaths, contextPath); }
        var authoredLootIds = loot.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        var canonicalLootDependencies = canonical.LootCards.Where(type => !authoredLootIds.Contains(type.Id)).ToList();

        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        void MapPaths(string field, int canonicalCount, IReadOnlyList<string> localPaths)
        {
            for (var index = 0; index < localPaths.Count; index++) paths[$"{field}[{canonicalCount + index}]"] = localPaths[index];
        }
        MapPaths("equipmentTypes", canonical.Equipment.Length, equipmentPaths);
        MapPaths("itemTypes", canonical.Items.Length, itemPaths);
        MapPaths("lootCardDefinitions", canonicalLootDependencies.Count, lootPaths);
        var resolved = new ResolvedContext([.. canonical.Equipment, .. equipment], [.. canonical.Items, .. items], [.. canonical.LootCards, .. loot],
            units, context.UnitTypes.Select(type => type.Id).ToHashSet(StringComparer.Ordinal));
        try { AbilityContent.ValidateObjectCatalogs(resolved.Equipment, resolved.Items, [.. canonicalLootDependencies, .. loot], units); }
        catch (ArgumentException error) { throw Rebase(error, paths, contextPath); }
        return resolved;
    }

    private static ContentEditorException Rebase(ArgumentException error, IReadOnlyDictionary<string, string> paths, string contextPath)
    {
        var runtimePath = ScenarioValidationContext.PathOf(error) ?? contextPath;
        var match = paths.FirstOrDefault(pair => runtimePath == pair.Key || runtimePath.StartsWith(pair.Key + ".", StringComparison.Ordinal));
        var path = match.Key is null ? runtimePath : match.Value + runtimePath[match.Key.Length..];
        var message = match.Key is null ? error.Message : error.Message.Replace(match.Key, match.Value, StringComparison.Ordinal);
        return Error(path.Length == 0 ? "context" : path, "validation", message);
    }

    private static void Replace<T>(List<T> definitions, List<string> paths, T candidate, string? existingId, Func<T, string> identity)
        where T : class
    {
        if (existingId is null)
        {
            definitions.Add(candidate);
            paths.Add("definition");
            return;
        }
        var matches = definitions.Select((definition, index) => (definition, index))
            .Where(row => row.definition is not null && identity(row.definition) == existingId).Select(row => row.index).ToArray();
        if (string.IsNullOrWhiteSpace(existingId) || matches.Length != 1)
            throw Error("existingId", "invalidEditTarget", "An edit must identify exactly one existing local definition in its dependency context.");
        if (identity(candidate) != existingId)
            throw Error("definition.id", "immutableId", "Keep the existing Type ID when editing. Create a copy to use a new ID.");
        definitions[matches[0]] = candidate;
        paths[matches[0]] = "definition";
    }

    private static ContentEditorResponse Invalid(ContentEditorDiagnostic diagnostic) => new(false, [diagnostic]);
    private static ContentEditorException Error(string path, string code, string message) => new(new(path, code, message));
    private static ContentEditorDiagnostic JsonDiagnostic(JsonException error, string root = "")
    {
        var suffix = error.Path is { } path && path.StartsWith('$') ? path[1..].TrimStart('.') : error.Path;
        var field = string.IsNullOrEmpty(suffix) ? root : string.IsNullOrEmpty(root) ? suffix : root + "." + suffix;
        return new(string.IsNullOrEmpty(field) ? "request" : field, "json", error.Message);
    }
    private static async Task<string> Read(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
    }
}
