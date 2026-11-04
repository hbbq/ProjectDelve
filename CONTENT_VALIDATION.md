# Standalone content validation and dependencies

Typed Ability Costs are inspected independently through `CostCatalog`; normal
content validation accepts them for supported active mechanic/source/timing
profiles and rejects them on Passives and intrinsic lifecycles. Dependency
discovery reads each `SpendResource` declaration directly
and reports its Resource Type and indexed cost field path, alongside existing
mechanic dependencies. Reference existence uses the supplied Resource catalog,
never generated wording or an ambient canonical lookup.

`AbilityContent.Validate` checks the same executable contract used by normal
composition. `CostCatalog.Validate` accepts an optional explicit
`availableResourceIds` set to validate reference existence; omitting that set
checks reference shape only. Generated descriptions prepend the ordered cost
requirements to the existing mechanic description, including overrides. This
covers all extracted invocation payments; Machine recipe exchanges remain Machine-owned.

Engine exposes definition inspection without creating a scenario, board, Unit or
GameState. Milestone 2 adds [explicit composition and captured runtime catalogs](CONTENT_COMPOSITION.md)
using these shared checks. [Content Workspace](CONTENT_LIBRARY.md) persistence also
uses these APIs. [Scenario exports](SCENARIO_CONTENT_REFERENCES.md) embed
the effective dependency closure without revision pins.
JSON provider validation and export use these APIs too; see [CONTENT_PACKAGES.md](CONTENT_PACKAGES.md).

```csharp
var equipment = new EquipmentDefinition("bell", "Bell") {
    LocalAbilities = [new("summon", "Summon", AbilityTiming.Action,
        new AdjacentUnitSummonMechanic("my-unit", Posture.Lying))]
};
var authored = new ContentDefinition(equipment,
    new ContentSource("equipment.json", "my-package", "equipmentTypes[0]"));
var structureErrors = ContentDefinitions.Validate(authored);
var direct = ContentDefinitions.Dependencies(equipment);
var graph = new ContentDependencyGraph([
    authored,
    new ContentDefinition(new UnitType("my-unit", 1, 1, 1, 1, 1))
]);
var referenceErrors = graph.Diagnostics;
var closure = graph.Closure([authored.Identity]);
```

`ValidateStructure` is the throwing equivalent of `Validate`. Both reuse existing
family validators and Ability/Mechanic Catalog contracts. Validation returns the
first structural failure per definition, matching existing throwing validators;
graph validation collects failures across definitions. Structure validation does
not resolve summon or Loot reward references. Scenario-local authoring retains Terrain compatibility restrictions. Reference existence
is checked against supplied definitions; Mechanic Catalog checks reference shape.

Supported values are UnitType, EquipmentDefinition, ItemDefinition,
LootCardDefinition, LootPreset, WorldCardDefinition, WorldDeckDefinition,
TerrainDefinition, TerrainAssemblyDefinition, FixedComponentDefinition,
ResourceType and MachineType. WorldDeckDefinition is an inspection envelope for
existing WorldCardQuantity rows; it does not add a runtime deck format.

`Dependencies` reads concrete typed fields, including local Ability payloads,
Loot rewards, preset quantities, World quantities, Terrain constituents,
production, Resource Storage and Machine operations. Explicit references,
explicit mechanic and Cost requirements and embedded owned payloads have distinct kinds.
Embedded edges retain the actual payload, including same-ID Resource variants.
Legacy enum Loot quantities use the existing typed compatibility adapter.
The dynamic All Loot preset discovers cards from the supplied graph definitions;
its standalone dependency list has no fixed card IDs. Legacy Unit terrain
restrictions expose the corresponding Terrain Type reference.

`ContentDependencyGraph` indexes supplied reusable definitions by family and Type
ID, without canonical fallback. Duplicate identities produce conflict diagnostics;
callers must require empty Diagnostics before consuming the graph as valid.
Terrain and Terrain assemblies share one identity namespace. Missing targets and
targets present only in another family are diagnosed separately. Nested Terrain
assemblies are rejected and composition checks reuse TerrainAssemblyContent.
Unit cycles remain valid. Direct edges and closure are sorted by family, ordinal
Type ID and (for edges) field path. Closure excludes roots unless reached through
an edge, includes missing requirements, and follows embedded constituent payloads.

`ContentDiagnostic` adds owner identity, optional ContentSource and dependency
identity around existing validation failures. Paths are relative to the owner;
combine them with ContentSource.Path in an editor. Validators without existing
field annotations report the definition root. Provenance is external to gameplay
models. ScenarioValidationContext continues serving existing scenario paths.
Conflict diagnostics also carry the other declaration's source in RelatedSource.

Resources and Machines remain inline owned definitions. Validate them and inspect
their dependencies directly; do not put them into the reusable graph index.
The optional `availableResources` graph argument supplies supported Resource IDs
for explicit production and explicit mechanic and Cost requirements. It accepts repeated
IDs and does not assert that payloads with the same ID are interchangeable.
Embedded Resource payloads require no external resolution. Independent library
Resources/Machines still need an approved reusable-reference and variant identity
model, typed authoring/serialization changes and resolution rules before they can
be globally indexed. Cell Markers are not advertised as an authorable family.

Scenario/runtime checks still own placement, finite supply, controllers,
ownership, current capacity, Posture and legal choices. Explicit new-game
composition preserves scenario-local Unit overrides. Runtime lookups use captured
definitions exclusively; see CONTENT_COMPOSITION.md. Unit stat-domain and Machine
operation checks are shared with runtime validation; no dummy state is used by
standalone validation.
