# Explicit content composition and game snapshots

Engine composes selected provider data before gameplay. C# rosters and
[JSON packages](CONTENT_PACKAGES.md) are providers. The separate
[persistent Content Library](CONTENT_LIBRARY.md) does not enter game composition implicitly.
Definitions retain their existing typed models and storage-independent identities.

```csharp
// The ordinary scenario adapter selects current canonical content at new-game creation.
var game = GameEngine.CreateGame(scenario);

// An explicit provider bundle replaces that default, including when intentionally empty.
var selected = ContentBundle.Canonical();
var explicitGame = GameEngine.CreateGame(scenario, selected);

// Compose multiple providers and an optional scenario-local bundle independently.
var effective = ContentComposition.Compose([providerA, providerB], scenarioLocal);
var capturedGame = GameEngine.CreateGame(scenario, effective);
```

`ContentBundle` contains immutable arrays of Units, Equipment, Items, Loot Cards,
Loot presets, World Cards and default quantities, Terrain, Fixed Components,
authored Terrain assemblies, and named Resource definitions. `Source` annotates
provider diagnostics outside gameplay data. Omitted bundle collections are empty.
Passing an empty bundle never installs canonical definitions. A scenario that
requires absent terrain, a preset or another definition fails validation.

`Compose` returns `EffectiveContent`, with materialized `Definitions` and the
Milestone 1 dependency graph. It validates structure, references and collisions
using [shared validation](CONTENT_VALIDATION.md). Identity is family + Type ID;
Terrain assemblies share the Terrain namespace. Provider order preserves authored
enumeration but never resolves collisions. Duplicate provider identities are
rejected even if a later scenario override would hide them. Only the separate
scenario-local Unit collection can override a provider Unit. Other local family
collisions fail. Historical scenario Loot aliases are normalized by the ordinary
new-game adapter before composition, without changing the authored scenario.

The `ContentBundle` new-game overload composes the scenario's local collections.
The `EffectiveContent` overload accepts an already final catalog: the caller must
have included those local declarations in `Compose`; they are not applied twice.
Terrain assembly declarations materialize into Terrain definitions containing
their surface and component payloads. Effective bundles carry those resolved
Terrain definitions rather than registering authored and resolved versions twice.

Composition failures throw `ContentCompositionException` with typed `Diagnostics`.
Each diagnostic retains owner identity, reference path and available provider
document/package/row location. Scenario creation translates these to the existing
ArgumentException/ScenarioValidationContext boundary and authored scenario paths.

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

## Remaining boundaries

Named production and implicit Resource requirements have an explicit lookup list.
This does not turn embedded Resource or Machine variants into globally reusable
definitions. Same-ID inline variants preserve their actual payloads. Independent
library Resources/Machines still need a reusable-reference and variant model.
Cell Markers retain their established concrete representation.

Canonical JSON providers are documented in [CONTENT_PACKAGES.md](CONTENT_PACKAGES.md).
Library package revisions are documented in [CONTENT_LIBRARY.md](CONTENT_LIBRARY.md).
Scenario reference pinning, promotion, inheritance and asset ownership remain deferred. Generator pools,
algorithms, authored scenarios, client-side rules and game rules are unchanged.
