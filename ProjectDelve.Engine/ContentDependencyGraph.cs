using System.Collections.Immutable;

namespace ProjectDelve.Engine;

// Inspection only: this never composes runtime catalogs or consults canonical fallbacks.
public sealed class ContentDependencyGraph
{
    private readonly Dictionary<ContentIdentity, ContentDefinition> definitions;
    private readonly Dictionary<ContentIdentity, ImmutableArray<ContentDependency>> edges;
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }
    public ContentDependencyGraph(IEnumerable<ContentDefinition> supplied, IEnumerable<ResourceType>? availableResources = null)
    {
        definitions = [];
        edges = [];
        List<ContentDiagnostic> diagnostics = [];
        var resourceIds = (availableResources ?? []).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in supplied.OrderBy(d => d.Identity.Family).ThenBy(d => d.Identity.TypeId, StringComparer.Ordinal)
            .ThenBy(d => d.Source?.Package, StringComparer.Ordinal).ThenBy(d => d.Source?.Document, StringComparer.Ordinal)
            .ThenBy(d => d.Source?.Path, StringComparer.Ordinal))
        {
            if (definition.Identity.Family is ContentFamily.Resource or ContentFamily.Machine)
                throw new ArgumentException("Resources and Machines remain owned inline definitions; inspect them with ContentDefinitions instead of indexing them globally.");
            if (!definitions.TryAdd(definition.Identity, definition))
                diagnostics.Add(new(definition.Identity, "", "conflictingDefinition", "Multiple supplied definitions have this identity.", definition.Source, definition.Identity, definitions[definition.Identity].Source));
        }
        foreach (var (identity, definition) in definitions)
        {
            var errors = ContentDefinitions.Validate(definition);
            diagnostics.AddRange(errors);
            // Malformed declarations are diagnosed before dependency traversal.
            edges[identity] = errors.IsEmpty ? ContentDefinitions.Dependencies(definition.Value) : [];
            // The established dynamic All preset selects every card in the supplied catalog.
            if (errors.IsEmpty && definition.Value is LootPreset { IsDynamic: true })
                edges[identity] = definitions.Keys.Where(key => key.Family == ContentFamily.LootCard)
                    .OrderBy(key => key.TypeId, StringComparer.Ordinal)
                    .Select(key => new ContentDependency(key, "composition", ContentDependencyKind.ImplicitMechanic)).ToImmutableArray();
        }
        IEnumerable<ContentDependency> Expand(IEnumerable<ContentDependency> dependencies, HashSet<object> seen)
        {
            foreach (var dependency in dependencies)
            {
                yield return dependency;
                if (dependency.EmbeddedDefinition is {} value && seen.Add(value))
                    foreach (var child in Expand(ContentDefinitions.Dependencies(value), seen))
                        yield return child with { Path = dependency.Path + "." + child.Path };
            }
        }
        foreach (var (identity, dependencies) in edges)
        foreach (var dependency in Expand(dependencies, new(ReferenceEqualityComparer.Instance)))
        {
            if (dependency.Kind == ContentDependencyKind.Embedded || dependency.Target.Family == ContentFamily.Resource && resourceIds.Contains(dependency.Target.TypeId)) continue;
            if (!definitions.ContainsKey(dependency.Target))
            {
                var mismatch = definitions.Keys.Any(key => key.TypeId == dependency.Target.TypeId) || resourceIds.Contains(dependency.Target.TypeId);
                diagnostics.Add(new(identity, dependency.Path, mismatch ? "familyMismatch" : "missingReference",
                    $"Required {dependency.Target.Family} '{dependency.Target.TypeId}' was not supplied.", definitions[identity].Source, dependency.Target));
            }
        }
        foreach (var (identity, definition) in definitions)
        {
            if (definition.Value is not TerrainAssemblyDefinition assembly || !ContentDefinitions.Validate(definition).IsEmpty) continue;
            if (definitions.GetValueOrDefault(new(ContentFamily.Terrain, assembly.SurfaceId))?.Value is TerrainAssemblyDefinition)
                diagnostics.Add(new(identity, "surfaceId", "invalidComposition", "Nested Terrain assemblies and circular composition are unsupported.", definition.Source, new(ContentFamily.Terrain, assembly.SurfaceId)));
            else if (definitions.GetValueOrDefault(new(ContentFamily.Terrain, assembly.SurfaceId))?.Value is TerrainDefinition surface &&
                assembly.Components.All(c => definitions.ContainsKey(new(ContentFamily.FixedComponent, c.ComponentId))))
            {
                try { TerrainAssemblyContent.Compose(assembly, surface, assembly.Components.Select(c => (FixedComponentDefinition)definitions[new(ContentFamily.FixedComponent, c.ComponentId)].Value)); }
                catch (ArgumentException error) { diagnostics.Add(new(identity, "", "invalidComposition", error.Message, definition.Source)); }
            }
        }
        foreach (var (identity, definition) in definitions.Where(p => p.Key.Family == ContentFamily.Terrain))
        {
            HashSet<string> path = new(StringComparer.Ordinal) { identity.TypeId };
            object current = definition.Value;
            string? Result(object value) => value switch
            {
                TerrainDefinition terrain => terrain.DestroyedTerrainTypeId,
                TerrainAssemblyDefinition assembly => assembly.DestroyedTerrainTypeId,
                _ => null
            };
            while (Result(current) is { } id && definitions.TryGetValue(new(ContentFamily.Terrain, id), out var next))
            {
                if (!path.Add(id))
                {
                    diagnostics.Add(new(identity, "destroyedTerrainTypeId", "invalidDestruction", "Circular Terrain destruction is not allowed.", definition.Source));
                    break;
                }
                if (next.Value is TerrainDefinition { LegacyKind: TerrainKind.Rail })
                    diagnostics.Add(new(identity, "destroyedTerrainTypeId", "invalidDestruction", "Destroyed Terrain cannot require Rail direction setup.", definition.Source));
                current = next.Value;
            }
        }
        Diagnostics = diagnostics.OrderBy(d => d.Owner.Family).ThenBy(d => d.Owner.TypeId, StringComparer.Ordinal)
            .ThenBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToImmutableArray();
    }
    public ImmutableArray<ContentDependency> DirectDependencies(ContentIdentity identity) => edges.GetValueOrDefault(identity, []);
    // Includes missing and implicit requirements. Embedded payloads remain attached to their owning edges.
    // Roots are excluded unless reached as dependencies (including self-summoning).
    public ImmutableArray<ContentIdentity> Closure(IEnumerable<ContentIdentity> roots)
    {
        HashSet<ContentIdentity> visited = [], result = [];
        var pending = new Stack<ContentIdentity>(roots);
        var embedded = new Stack<object>();
        var visitedEmbedded = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Include(ContentDependency dependency)
        {
            result.Add(dependency.Target);
            if (dependency.EmbeddedDefinition is {} value) embedded.Push(value);
            else pending.Push(dependency.Target);
        }
        while (pending.Count > 0 || embedded.Count > 0)
        {
            if (embedded.TryPop(out var value))
            {
                if (visitedEmbedded.Add(value)) foreach (var dependency in ContentDefinitions.Dependencies(value)) Include(dependency);
                continue;
            }
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            foreach (var dependency in DirectDependencies(current)) Include(dependency);
        }
        return result.OrderBy(d => d.Family).ThenBy(d => d.TypeId, StringComparer.Ordinal).ToImmutableArray();
    }
}
