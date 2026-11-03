# Scope composition and game snapshots

Engine resolves complete typed definitions by **Scenario > Workspace > Canonical**. Identity is Content Family plus Type ID (Terrain assemblies share the Terrain namespace). Closer definitions replace entire definitions; no fields merge. Duplicate identities within any single scope fail even when a closer scope would hide them. References resolve against the final catalog, so Equipment grants and Summoning use the winning Unit definition.

```csharp
var effective = ContentComposition.ComposeScopes(canonical, workspace, scenarioLocal);
var game = GameEngine.CreateGame(scenario, effective.Definitions);
```

`ContentComposition.Compose` remains the strict composition boundary for peer canonical providers; `ComposeScopes` implements authoring precedence. Both produce the same typed `EffectiveContent` and validated dependency graph. C# roster and JSON definitions use one Engine path. Omitted collections are empty; empty Workspace is valid. A nonempty higher default World Deck replaces the lower deck; an empty deck in a bundle means no authored deck at that scope.

The Web host supplies current Workspace plus Canonical to Designer, generation metadata and new-game adapters. Scenario-local collections then replace matching identities. Dependency-only Unit definitions may exist without joining initial activation order; placement and agency still require explicit scenario selection.

Canonical publication uses explicit ownership transfer, described in [promotion](CONTENT_PROMOTION.md), to maintain a single effective provider per identity. Running games never consult mutable authoring scopes.

## Snapshot boundary

The authoritative snapshot uses existing GameState family collections and the
Board's Terrain catalog. There is no second runtime catalog. Game creation keeps
declared Unit order, then captures the dependency graph closure for initial Units
and all selected Equipment and Item definitions. This includes transitive summons
from inventory rewards that may be acquired later. All selected object, Loot,
World, Terrain and named Resource definitions are captured, including definitions
not initially instantiated. Fixed Components and assembly surfaces are embedded
in resolved Terrain. Machine and inline Resource payloads remain embedded in their
owning objects, operations, storage or output tables.

`ActivationTypeOrder` records the previous declared/first-summoned enumeration
independently of early definition capture. Placement appends a newly summoned Type
to that order; current bag membership is unchanged. Capturing future Types does
not add tokens or change injected random streams.

Live Loot presets (including dynamic All) and default World quantities resolve
only at creation against the selected provider. Actual Loot objects, World cards,
materialized World composition and all remaining physical piles are serialized.
Editable scenario drafts keep their live references and are not frozen by preview
or Playtest. Canonical and local definitions are not mutated by composition.

Runtime Unit lookup with a supplied list is exact. Summoning, placement, geometry,
Loot rewards, Equipment, Items and World effects use captured definitions. Named
environmental production uses `GameState.ResourceDefinitions`. Spending and
Machine output events retain the actual owned Resource payload. Legacy enum IDs
are translated through fixed identity mappings; they do not retrieve card effects
or terrain properties from rosters. Parameter validation checks reference shape;
reference existence is checked against the supplied graph, including Resource
references, rather than the current canonical roster.

`ContentComposition.ValidateSnapshot(game)` validates family definitions and
dependencies without canonical resolution. Normal scenario/round validation also
calls it, then checks runtime placement, inventories, deck locations, counters and
other existing legality constraints. Invalid missing runtime lookups throw rather
than inserting definitions. Low-level manually constructed GameStates must supply
their own content; catalog properties default to empty. The three-argument Board
constructor remains a convenience for explicitly authoring a canonical board;
runtime copy and JSON reconstruction use its captured Terrain catalog instead.

Current saves require explicit family catalogs and Terrain snapshot version 2.
Historical runtime repair from canonical Loot, Equipment or Terrain has been
removed. Scenario import compatibility remains. Save/resume and deterministic
replay retain pending operations, generic Ability counters, physical inventories
and source definitions. Public gameplay projections still pass through the
existing observation boundary: unrevealed Loot and World definitions and hidden
Unit/object definitions are removed; named Resource metadata is limited to visible
production references. Authoring endpoints remain separate and omniscient.
Typed GameResponse consumers retain the supplied authoritative Presentation, including placement previews,
rather than rebuilding it from public state that deliberately omits definitions.
Public responses are observations, not resumable save files.

## Lower-level provider boundaries

Nonempty Ability Costs are supported on active declarations through shared
commitment/payment. Normal composition and snapshot validation check their
supported source/timing contracts and Resource dependencies.
Empty Costs remain omitted from serialized definitions. Named production and implicit Resource requirements use an explicit lookup list; embedded Resource/Machine variants retain their owned payloads. Machines and Cell Markers gain no new authoring family.

Canonical JSON providers are documented in [CONTENT_PACKAGES.md](CONTENT_PACKAGES.md). [Workspace storage](CONTENT_LIBRARY.md) is mutable and revision-free. [Scenario exports](SCENARIO_CONTENT_REFERENCES.md) embed effective definitions for portability. [Canonical promotion](CONTENT_PROMOTION.md) supports new identities and explicit replacement ownership. Historical revision/pin services remain only as legacy infrastructure. Generator selection policies, authored scenarios and gameplay rules are unchanged.
