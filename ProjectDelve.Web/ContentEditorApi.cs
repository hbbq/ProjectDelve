using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ContentEditorContext
{
    public List<TerrainDefinition> TerrainTypes { get; init; } = [];
    public List<TerrainAssemblyDefinition> TerrainAssemblies { get; init; } = [];
    public List<FixedComponentDefinition> FixedComponentTypes { get; init; } = [];
    public List<UnitType> UnitTypes { get; init; } = [];
    public List<EquipmentDefinition> EquipmentTypes { get; init; } = [];
    public List<ItemDefinition> ItemTypes { get; init; } = [];
    public List<LootCardDefinition> LootCardDefinitions { get; init; } = [];
    public List<ScenarioContentReference>? ContentReferences { get; init; }
}
public sealed record ContentEditorRequest(string Family, JsonElement Definition,
    string? ExistingId = null, ContentEditorContext? Context = null, string Destination = "scenario");
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
        app.MapPost("/api/content/validate", async (HttpRequest request) =>
        {
            try
            {
                var input = JsonSerializer.Deserialize<ContentEditorRequest>(await Read(request), Json)
                    ?? throw new JsonException("A content validation request is required.");
                var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
                var workspace = app.Services.GetRequiredService<ContentWorkspace>().Snapshot().Definitions;
                var result = Validate(input, ContentComposition.ComposeScopes(canonical, workspace).Definitions);
                if (result.Catalog is { } catalog)
                {
                    var annotated = ContentBrowserCatalog.WorkspaceOrigins(catalog, workspace, canonical);
                    result = result with { Catalog = annotated, Entry = result.Entry is { } entry ? annotated.Entries.Single(row => row.Key == entry.Key) : null };
                }
                return Results.Ok(result);
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
                var canonical = app.Services.GetRequiredService<CanonicalContent>().Bundle;
                var workspace = app.Services.GetRequiredService<ContentWorkspace>().Snapshot().Definitions;
                var effective = ContentComposition.ComposeScopes(canonical, workspace).Definitions;
                var resolved = Resolve(context, contextPath: "", canonical: effective);
                var catalog = Origins(ContentBrowserCatalog.Definitions(resolved.Equipment, resolved.Items, resolved.Loot, resolved.Units, resolved.LocalUnitIds, effective, resolved.Effective), context);
                return Results.Ok(ContentBrowserCatalog.WorkspaceOrigins(catalog, workspace, canonical));
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
        if (request.Family is not ("units" or "equipment" or "items" or "lootCards" or "terrain" or "terrainAssemblies"))
            return Invalid(new("family", "unsupportedFamily", "This editor supports Units, Equipment, Items, Loot Cards, Terrain and Terrain Assemblies."));
        if (request.Destination is not ("scenario" or "workspace"))
            return Invalid(new("destination", "unsupportedDestination", "Choose scenario or workspace authoring."));
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
            var resolved = Resolve(request.Context ?? new(), candidate, request.ExistingId, canonical: canonical, workspace: request.Destination == "workspace");
            var catalog = Origins(ContentBrowserCatalog.Definitions(resolved.Equipment, resolved.Items, resolved.Loot, resolved.Units, resolved.LocalUnitIds, canonical, resolved.Effective), request.Context ?? new(), candidate);
            var id = ContentDefinitions.Identity(candidate).TypeId;
            return new(true, [])
            {
                Catalog = catalog,
                Definition = JsonSerializer.SerializeToElement(candidate, candidate.GetType(), Json),
                Entry = catalog.Entries.Single(entry => entry.Family == request.Family && entry.Id == id)
            };
        }
        catch (ContentEditorException error) { return Invalid(error.Diagnostic); }
        catch (ContentCompositionException error) { return Invalid(new("contentReferences", "validation", error.Message)); }
    }

    internal static object ParseDefinition(ContentEditorRequest request) => request.Family switch
    {
        "terrain" => request.Definition.Deserialize<TerrainDefinition>(Json) ?? throw new JsonException("A Terrain definition is required."),
        "terrainAssemblies" => request.Definition.Deserialize<TerrainAssemblyDefinition>(Json) ?? throw new JsonException("A Terrain Assembly definition is required."),
        "units" => request.Definition.Deserialize<UnitType>(Json) ?? throw new JsonException("A Unit Type definition is required."),
        "equipment" => request.Definition.Deserialize<EquipmentDefinition>(Json) ?? throw new JsonException("An Equipment definition is required."),
        "items" => request.Definition.Deserialize<ItemDefinition>(Json) ?? throw new JsonException("An Item definition is required."),
        "lootCards" => request.Definition.Deserialize<LootCardDefinition>(Json) ?? throw new JsonException("A Loot Card definition is required."),
        _ => throw new ArgumentException("This editor supports Units, Equipment, Items, Loot Cards, Terrain and Terrain Assemblies.")
    };

    private sealed record ResolvedContext(List<EquipmentDefinition> Equipment, List<ItemDefinition> Items, List<LootCardDefinition> Loot,
        List<UnitType> Units, IReadOnlySet<string> LocalUnitIds, ContentBundle Effective);
    private sealed class ContentEditorException(ContentEditorDiagnostic diagnostic) : Exception(diagnostic.Message)
    { public ContentEditorDiagnostic Diagnostic => diagnostic; }

    private static ResolvedContext Resolve(ContentEditorContext context, object? candidate = null, string? existingId = null,
        string contextPath = "context", ContentBundle? canonical = null, bool workspace = false)
    {
        canonical ??= ContentBundle.Canonical();
        canonical = ScenarioContentReferences.Compose(canonical, context.ContentReferences).Definitions;
        string Path(string field) => contextPath.Length == 0 ? field : contextPath + "." + field;
        if (context.UnitTypes is null) throw Error(Path("unitTypes"), "required", "Unit dependencies must be an array.");
        if (context.EquipmentTypes is null) throw Error(Path("equipmentTypes"), "required", "Equipment dependencies must be an array.");
        if (context.ItemTypes is null) throw Error(Path("itemTypes"), "required", "Item dependencies must be an array.");
        if (context.LootCardDefinitions is null) throw Error(Path("lootCardDefinitions"), "required", "Loot Card dependencies must be an array.");
        if (context.TerrainTypes is null || context.TerrainAssemblies is null || context.FixedComponentTypes is null)
            throw Error(Path("terrainTypes"), "required", "Terrain, assemblies and component dependencies must be arrays.");
        var terrain = context.TerrainTypes.ToList();
        var assemblies = context.TerrainAssemblies.ToList();
        var terrainPaths = Enumerable.Range(0, terrain.Count).Select(i => $"{Path("terrainTypes")}[{i}]").ToList();
        var assemblyPaths = Enumerable.Range(0, assemblies.Count).Select(i => $"{Path("terrainAssemblies")}[{i}]").ToList();
        if (candidate is TerrainDefinition terrainDefinition) Replace(terrain, terrainPaths, terrainDefinition, existingId, t => t.Id);
        if (candidate is TerrainAssemblyDefinition assemblyDefinition) Replace(assemblies, assemblyPaths, assemblyDefinition, existingId, t => t.Id);
        var authoredUnits = context.UnitTypes.ToList();
        var localUnitPaths = Enumerable.Range(0, authoredUnits.Count).Select(index => $"{Path("unitTypes")}[{index}]").ToList();
        if (candidate is UnitType unit)
        {
            Replace(authoredUnits, localUnitPaths, unit, existingId, type => type.Id);
        }
        for (var index = 0; index < authoredUnits.Count; index++)
        {
            if (authoredUnits[index] is null) throw Error(localUnitPaths[index], "required", "A Unit Type definition is required.");
            var diagnostic = ContentDefinitions.Validate(new(authoredUnits[index])).FirstOrDefault();
            if (diagnostic is not null)
                throw Error(localUnitPaths[index] + (diagnostic.Path.Length == 0 ? "" : "." + diagnostic.Path), diagnostic.Code, diagnostic.Message);
        }
        List<UnitType> units;
        var unitPaths = Enumerable.Range(0, authoredUnits.Count).ToDictionary(index => $"unitTypes[{index}]", index => localUnitPaths[index]);
        try { units = UnitContent.Resolve(authoredUnits, canonical.Units); }
        catch (ArgumentException error) { throw Rebase(error, unitPaths, contextPath); }

        var equipment = context.EquipmentTypes.ToList();
        var items = context.ItemTypes.ToList();
        var loot = context.LootCardDefinitions.ToList();
        var equipmentPaths = Enumerable.Range(0, equipment.Count).Select(index => $"{Path("equipmentTypes")}[{index}]").ToList();
        var itemPaths = Enumerable.Range(0, items.Count).Select(index => $"{Path("itemTypes")}[{index}]").ToList();
        var lootPaths = Enumerable.Range(0, loot.Count).Select(index => $"{Path("lootCardDefinitions")}[{index}]").ToList();
        if (candidate is EquipmentDefinition equipmentDefinition)
            Replace(equipment, equipmentPaths, equipmentDefinition, existingId, type => type.Id);
        else if (candidate is ItemDefinition itemDefinition)
            Replace(items, itemPaths, itemDefinition, existingId, type => type.Id);
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
        ContentBundle effective;
        try
        {
            var local = new ContentBundle
            {
                Terrain = [.. terrain], Assemblies = [.. assemblies], FixedComponents = [.. context.FixedComponentTypes],
                Units = [.. authoredUnits], Equipment = [.. equipment], Items = [.. items], LootCards = [.. loot],
                Source = new(Package: "content-editor")
            };
            effective = (workspace ? ContentComposition.ComposeScopes(canonical, workspace: local)
                : ContentComposition.ComposeScopes(canonical, scenarioLocal: local)).Definitions;
        }
        catch (ContentCompositionException error)
        {
            var diagnostic = error.Diagnostics[0];
            if (diagnostic.Code == "conflictingDefinition" && candidate is not null && ContentDefinitions.Identity(candidate) == diagnostic.Owner)
                throw Error("definition.id", diagnostic.Code, diagnostic.Message);
            var localPath = diagnostic.Owner.Family switch
            {
                ContentFamily.Terrain => terrain.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var ti && ti >= 0 ? terrainPaths[ti]
                    : assemblies.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var ai && ai >= 0 ? assemblyPaths[ai] : null,
                ContentFamily.Unit => authoredUnits.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var i && i >= 0 ? localUnitPaths[i] : null,
                ContentFamily.Equipment => equipment.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var i && i >= 0 ? equipmentPaths[i] : null,
                ContentFamily.Item => items.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var i && i >= 0 ? itemPaths[i] : null,
                ContentFamily.LootCard => loot.FindLastIndex(t => t.Id == diagnostic.Owner.TypeId) is var i && i >= 0 ? lootPaths[i] : null,
                _ => null
            };
            throw Error((localPath ?? contextPath) + (diagnostic.Code == "conflictingDefinition" ? ".id" : "") + (diagnostic.Path.Length == 0 ? "" : "." + diagnostic.Path), diagnostic.Code, diagnostic.Message);
        }
        catch (ArgumentException error)
        {
            throw Error(candidate is null ? contextPath : "definition", "validation", error.Message);
        }
        var resolved = new ResolvedContext([.. effective.Equipment], [.. effective.Items], [.. effective.LootCards],
            [.. effective.Units], authoredUnits.Select(t => t.Id).ToHashSet(StringComparer.Ordinal), effective);
        return resolved with { LocalUnitIds = authoredUnits.Select(t => t.Id).ToHashSet(StringComparer.Ordinal) };
    }

    private static ContentBrowserResponse Origins(ContentBrowserResponse catalog, ContentEditorContext context, object? candidate = null)
    {
        var ids = context.UnitTypes.Select(t => new ContentIdentity(ContentFamily.Unit, t.Id))
            .Concat(context.TerrainTypes.Select(t => new ContentIdentity(ContentFamily.Terrain, t.Id)))
            .Concat(context.TerrainAssemblies.Select(t => new ContentIdentity(ContentFamily.Terrain, t.Id)))
            .Concat(context.EquipmentTypes.Select(t => new ContentIdentity(ContentFamily.Equipment, t.Id)))
            .Concat(context.ItemTypes.Select(t => new ContentIdentity(ContentFamily.Item, t.Id)))
            .Concat(context.LootCardDefinitions.Select(t => new ContentIdentity(ContentFamily.LootCard, t.Id))).ToHashSet();
        if (candidate is not null) ids.Add(ContentDefinitions.Identity(candidate));
        var result = catalog with { Entries = catalog.Entries.Select(entry =>
        {
            var family = entry.Family switch { "units" => ContentFamily.Unit, "equipment" => ContentFamily.Equipment,
                "items" => ContentFamily.Item, "lootCards" => ContentFamily.LootCard, "terrain" or "terrainAssemblies" => ContentFamily.Terrain, _ => (ContentFamily?)null };
            return family is not null && ids.Contains(new(family.Value, entry.Id)) ? entry with { Origin = entry.Family == "units" ? entry.Origin : "local", Availability = null } : entry;
        }).ToArray() };
        return ContentBrowserCatalog.Pinned(result, context.ContentReferences, context.UnitTypes.Select(t => t.Id).ToHashSet(StringComparer.Ordinal));
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
