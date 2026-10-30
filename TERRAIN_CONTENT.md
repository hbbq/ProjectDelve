# Terrain content, fixed components and environmental production

Terrain Types have stable Type IDs, display names and declared gameplay properties. Canonical C# and scenario-local JSON definitions resolve through the same per-game catalog. Movement, placement, pathfinding, geometric LOS and Fog of War consume the resolved properties; an unfamiliar Type ID does not need an Engine handler.

This follows the catalog boundary used by [Equipment](EQUIPMENT_CONTENT.md), [Abilities and Items](ABILITY_CONTENT.md) and [World Cards](WORLD_CARD_CONTENT.md). One Cell still contains one terrain tile. A Table rendered on a floor does not become a separately movable or targetable object. Typed production and fixed-component assemblies resolve into these same Terrain definitions before gameplay.

## Playable example: Fixed Furniture - Shared Components

Open **Home → Test Scenarios → Fixed Furniture - Shared Components → Play scenario**. The Stone Floor with Table near the Wizard and the Grass with Table farther east use the same canonical `table` component. The Guest Bed blocks movement without blocking LOS; the Garden Statue blocks both. The Campfire on Gravel emits warm presentation lighting, blocks movement, and has no damage or production mechanic.

Start a round, wait for the Wizard and choose **Stay**. Its ordinary Attack can target the eastern Practice Dummy across the Table; the southern Dummy is screened by the Statue. The Dummies have Do Nothing Orders, and there is no victory condition, World Deck or random Loot. **View & camera → Full Board** shows the complete arrangement. Each assembly occupies one Cell and represents one physical tile, including all its fixed furniture.

**Details → Open in Designer** exposes the assembled name, effective movement/LOS properties, surface, fixed component names and orientation. Choose an assembly in the Terrain palette to paint it; use **Use default (remove override)** to remove a placement. Undo/redo, saved drafts, recovery, JSON/DELVE1 import/export and **Test scenario** preserve the authored references. Creating a new component or assembly uses JSON in this milestone. Tests use dedicated fixtures, not this mutable example's layout.

## Playable example: Environmental Production - Three Sources

Open **Home → Test Scenarios → Environmental Production - Three Sources → Play scenario**. From left to right, the board contains ordinary non-producing Lava, Coal-producing Lava, and a scenario-local **Copper Orchard**. Start a round and inspect the Journal: ordinary Lava makes no rolls; Coal Lava makes two ordered d8 attempts; the Copper Orchard makes one attempt to produce Fruit. Resources appear on the selected neighboring Cells when placement succeeds. A failed attempt is not rerolled.

The Copper Orchard has a completely unfamiliar Type ID and uses the existing Tree appearance. Its LOS-blocking property remains active for normal vision and attacks; its production declaration ignores only the producing Cell's own terrain obstruction for its production LOS check. The appearance does not supply its resource, attempt count or rules.

There are no enemies, victory conditions, World Cards or random Loot. Choose **Stay**, then **Next round** to observe production against the Resources already on the board. With automatic choices disabled, choose **End Turn** when offered. **Restart current** restores the empty initial board. **Details → Open in Designer** exposes the declared production description, and ordinary painting, undo/redo, export and Playtest preserve the same declarations. **View & camera → Full Board** shows all three sources together in either Classic or 3D. These coordinates explain the current authored example; tests use dedicated fixtures rather than depending on this layout.

## Playable example: Local Terrain - Curiosity Room

Open **Home → Test Scenarios → Local Terrain - Curiosity Room → Play scenario**, or find that title under **Scenarios**. Its definition is in [library.json](ProjectDelve.Web/Content/Library/library.json). All three unfamiliar Terrain Types belong only to this scenario:

| Terrain | Passable | Blocks LOS | Appearance |
| --- | --- | --- | --- |
| Velvet Plinth | No | No | Existing Table recipe |
| Midnight Column | No | Yes | Existing Solid Rock recipe |
| Moon Paper | Yes | No | Unfamiliar recipe, using the generic fallback |

Start a round and wait for the Wizard's activation. Neither the Plinth immediately to the east nor the Column immediately to the south is a legal stopping Cell. Choose **Stay**, then inspect the ordinary Attack choices: the eastern Practice Dummy can be attacked across the Plinth; the southern Dummy is hidden from that attack by the Column. The Dummies do not move or attack. There is no victory condition, World Deck or random Loot. Restart restores the initial setup.

Use **Details → Open in Designer** to select these Terrain Types by name and paint more Cells. **Test scenario** plays the exported draft; **Menu → Back to Designer** returns to the authoring workspace. Import/export, recovery and undo/redo preserve the definitions and their references. Creating or changing a Terrain definition itself uses scenario JSON in this milestone.

These authored coordinates explain the current demonstration, not a test contract. Regression tests use dedicated test-owned scenarios; library checks only require valid loading and instantiation.

## Canonical declarations

Canonical content is declared in [TerrainRoster.cs](ProjectDelve.Engine/TerrainRoster.cs). [TerrainContent.cs](ProjectDelve.Engine/TerrainContent.cs) supplies the typed definitions, catalog resolution and validation. For example:

```csharp
new TerrainDefinition("velvet-plinth", "Velvet Plinth",
    Passable: false, BlocksLos: false)
{
    Presentation = new("StoneFloorWithTable")
};
```

`Passable` describes terrain traversal and ordinary stopping before occupancy, Unit restrictions and other rules are considered. `BlocksLos` is independent. Neither is inferred from a name, recipe, mesh, material or color.

`AllowsResourceMarkers` defaults to `true`. It preserves the separate rule that Lava cannot hold a Resource marker. It is not a substitute for Resource-production eligibility: production additionally requires passable terrain, no Machine and no existing Resource. In particular, making a Cell impassable does not automatically forbid an authored Resource there. Passable Terrain must allow Resource markers in this milestone: Transport Resource has no established behavior for entering a passable Cell that forbids its carried marker. That unsupported combination is rejected instead of inventing a drop or movement rule.

`Presentation.Recipe` references a visual recipe. Existing examples include `StoneFloor`, `Grass`, `StoneFloorWithTable` and `SolidRock`. Omission uses `generic`; an unknown recipe uses a generic visual fallback. Artwork never grants terrain properties. Lighting and aesthetic occlusion remain presentation-only and independent of gameplay LOS.

The roster produces ordinary immutable data, with no delegates or executable content. Its resolved definitions can be serialized and deserialized as the same typed catalog, including canonical compatibility metadata. A future canonical JSON provider can supply this model without a second execution path. Scenario-local catalogs add new Types; they do not replace canonical entries.

## Scenario-local JSON

This complete scenario defines an unfamiliar obstruction using supported properties:

```json
{
  "terrainTypes": [
    {
      "id": "velvet-plinth",
      "name": "An Unexpected Velvet Plinth",
      "passable": false,
      "blocksLos": false,
      "presentation": { "recipe": "StoneFloorWithTable" }
    }
  ],
  "board": {
    "width": 4,
    "height": 3,
    "cells": [
      { "position": { "x": 2, "y": 1 }, "terrainTypeId": "velvet-plinth" }
    ]
  },
  "unitTypeIds": ["wizard-type"],
  "units": [
    {
      "unitTypeId": "wizard-type", "sideId": "blue",
      "anchor": { "x": 1, "y": 1 }, "unitId": "observer"
    }
  ],
  "agency": [
    { "unitTypeId": "wizard-type", "sideId": "blue", "controller": "Human" }
  ],
  "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
  "loot": { "composition": [] }
}
```

Use `board.defaultTerrainTypeId` for a typed default surface. Omitted defaults retain Stone Floor. Explicit `terrainTypeId` and `defaultTerrainTypeId` values must resolve; a missing Type never silently becomes Stone Floor. Canonical IDs such as `stone-floor`, `grass` and `asphalt` can also be referenced through these fields.

IDs and category keys are case-sensitive, nonblank strings of at most 128 characters using ASCII letters, digits, `-`, `_`, `:` or `.`. Names are independent display text. Duplicate IDs, duplicate categories, missing required properties, malformed values and unsupported definition fields are rejected. Local IDs must not duplicate canonical IDs, and local definitions cannot claim a canonical `legacyKind`.

Definitions resolve in the current scenario/game only. The same local ID can have a different meaning in another scenario without modifying process-global content. Scenario JSON and DELVE1 preserve authored definitions; runtime saves preserve the resolved catalog. Restart creates the initial scenario again, while save/resume retains the running game's content and state. Replay uses the definitions associated with its authoritative snapshots.

## Unit-owned category restrictions

Terrain declares general categories; the Unit declares which categories it accepts:

```csharp
var surface = new TerrainDefinition("practice-track", "Practice Track", true, false)
{
    Categories = [TerrainCategories.RacingGround],
    Presentation = new("Asphalt")
};

var runner = new UnitType("practice-runner", 3, 1, 1, 1, 1)
{
    TerrainRestriction = new([TerrainCategories.RacingGround])
};
```

Equivalent JSON fields are:

```json
{
  "terrainTypes": [
    {
      "id": "practice-track", "name": "Practice Track",
      "passable": true, "blocksLos": false,
      "categories": ["racing-ground"],
      "presentation": { "recipe": "Asphalt" }
    }
  ],
  "unitTypes": [
    {
      "id": "practice-runner", "mov": 3, "rng": 1,
      "atk": 1, "def": 1, "hp": 1,
      "terrainRestriction": { "categories": ["racing-ground"] }
    }
  ]
}
```

The restriction accepts a Cell matching **any** category in its nonempty list. This does not grant passability or bypass occupancy, footprint, Edge, Phase-stopping or Fog of War rules. An obstructed matching surface remains obstructed under ordinary movement rules. A Unit with no restriction has no category requirement.

Only Asphalt among existing canonical Terrain carries `racing-ground`, preserving existing Kart layouts and legality. Declaring the category on new authored Terrain explicitly makes that Terrain match the same Unit requirement; the Terrain does not name a Kart or any vehicle. Legacy `terrainRestrictedMovement` input remains readable through its compatibility adapter.

## Fixed components and assemblies

The standard Designer Terrain palette includes **Campfire** (`campfire`): Grass plus the canonical `campfire` Fixed Component. It blocks movement but not gameplay LOS, has no Resource production or other gameplay effect, and reuses the existing Campfire model and warm presentation lighting. Place it with `"terrainTypeId": "campfire"`; no scenario-local definitions are needed. Existing authored Campfire assemblies keep their own surfaces and references unchanged.

To reuse the same canonical component on another suitable surface, declare a local assembly with that `surfaceId` and `"components": [{ "slot": "object", "componentId": "campfire" }]`. Its recipe supplies only presentation; its declared obstruction properties supply gameplay behavior.

Scenario JSON keeps three kinds of environmental content distinct:

- `terrainTypes` defines atomic Terrain, including its gameplay properties and optional production.
- `fixedComponentTypes` defines reusable fixed parts with independent `blocksMovement` and `blocksLos` properties and a presentation recipe.
- `terrainAssemblies` gives a new Terrain Type ID and display name to one `surfaceId` plus zero or more slotted component references.

For example, this fragment reuses the canonical Table on two surfaces and adds a scenario-local Bed:

```json
{
  "fixedComponentTypes": [
    {
      "id": "guest-bed", "name": "Guest Bed",
      "blocksMovement": true, "blocksLos": false,
      "presentation": { "recipe": "Bed", "layer": "Object" }
    }
  ],
  "terrainAssemblies": [
    {
      "id": "garden-table", "name": "Grass with Table", "surfaceId": "grass",
      "components": [{ "slot": "furniture", "componentId": "table" }]
    },
    {
      "id": "gravel-table", "name": "Gravel with Table", "surfaceId": "gravel",
      "components": [{ "slot": "furniture", "componentId": "table" }]
    },
    {
      "id": "guest-room", "name": "Stone Floor with Bed", "surfaceId": "stone-floor",
      "components": [{ "slot": "furniture", "componentId": "guest-bed", "quarterTurns": 1 }]
    }
  ],
  "board": {
    "width": 4, "height": 3,
    "cells": [
      { "position": { "x": 1, "y": 1 }, "terrainTypeId": "garden-table" },
      { "position": { "x": 2, "y": 1 }, "terrainTypeId": "guest-room" }
    ]
  }
}
```

Combine the fragment with ordinary scenario Unit, agency, objective and Loot settings. An assembly ID is a Terrain Type ID: Cells, defaults, Designer and gameplay reference it exactly like an atomic Terrain Type. No new Engine handler or per-assembly rendering branch is needed. Components do not have individual board positions, Instance IDs, ownership, HP, inventory, targets or Actions.

The matching C# declarations use the same catalog model:

```csharp
var bed = new FixedComponentDefinition("guest-bed", "Guest Bed",
    BlocksMovement: true, BlocksLos: false)
{
    Presentation = new("Bed", FixedComponentLayer.Object)
};
var room = new TerrainAssemblyDefinition("guest-room", "Stone Floor with Bed", "stone-floor")
{
    Components = [new("furniture", "guest-bed", QuarterTurns: 1)]
};
var terrain = TerrainCatalog.Resolve(local: null,
    assemblies: [room], fixedComponents: [bed]);
```

The Engine validates all references and resolves each assembly once. Effective passability is the surface's passability **and** the absence of a movement-blocking component. Effective LOS blocking is the surface's LOS blocking **or** any LOS-blocking component. Surface categories, Resource-marker permission and environmental production are preserved. Production still runs once for the resulting Cell; components cannot declare another producer. Unit restrictions, Phase stopping, footprints, geometric LOS, FoW and Resource eligibility consume the resulting ordinary Terrain properties through their existing queries.

Slots are stable keys within an assembly, not attachment points or free coordinates. Up to four distinct slots are supported, with at most one raised `Object` component; additional components must be flat `Decal` presentation layers. This prevents undefined furniture stacking. `quarterTurns` is optional presentation orientation from 0 through 3; it changes neither footprint nor rules. Omission keeps deterministic visual rotation. Invalid properties, duplicate IDs or slots, missing references and unsupported layers/orientations are rejected. Presentation may fall back for unknown recipes; component gameplay definitions must exist.

Assemblies cannot use another assembly as a surface; cycles and nested composition are rejected. Canonical Water, Rail and fixed-obstacle Terrain are not accepted as surfaces in this milestone. Choose an ordinary atomic surface such as Stone Floor, Grass, Gravel, Asphalt, Lava, or a suitable local definition. This preserves Water/Rail compatibility and avoids silently stacking existing fixed objects. The surface supplies the flat visual layer; raised geometry comes from the declared components.

Canonical Stone Floor with Table and Tree keep their existing Terrain IDs and resolved rules while declaring the same reusable Table/Tree components used by local assemblies. Their established material variants, object rotations and physical model placement remain stable. Rail retains its existing dedicated Gravel base, track geometry and outgoing direction rather than gaining a generalized component mechanic. Lava and Fruit Tree production remain on their existing declarations.

`Table` and `Tree` presentation recipes reuse existing transparent object art and Tabletop geometry. `Bed`, `Statue` and `Campfire` provide simple recipe geometry without custom assets; an unfamiliar component recipe uses a marked plinth for `Object`, or a flat question-mark emblem for `Decal`. The raised object stays centered; flat decals occupy bounded corner positions in declaration order. Assemblies render the declared surface and fixed layers in Classic, Enhanced and 3D; picking and highlights continue to address the Cell. The `Campfire` object recipe uses the existing local-light presentation infrastructure; its flat decal form does not emit light. Lighting and visible geometry never supply passability or gameplay LOS.

Resolved assembly data is immutable and self-contained for save/resume and replay. Authoring references remain in scenario JSON and Designer workspaces. Observation filters the containing Terrain Type; hidden assemblies and their component data are not revealed through a global component catalog. New definitions remain scoped to their scenario.

## Environmental resource production

`TerrainDefinition.Production` is optional. Its absence means that Terrain does not produce Resources. A producing Terrain uses the same typed definition and execution path whether it was declared in the canonical C# roster or scenario JSON.

The supported mechanic is `directionalResourceProduction`. Its placement policy is the existing eight surrounding Cells at range one, with a d8 selecting the attempted Cell. It is not an arbitrary placement expression or a general trigger system. Production always runs after Round Start World Cards and all their consequences, before Activation Bag population.

The canonical catalog distinguishes ordinary Lava from Coal Lava. Both are impassable, do not block gameplay LOS, forbid Resource markers on their own Cell, and use the existing Lava appearance and lighting. Only Coal Lava declares production. Fruit Tree declares the same mechanic with Fruit and its existing source-LOS exemption.

The typed C# declaration for an unfamiliar producer is ordinary content data:

```csharp
new TerrainDefinition("copper-orchard", "Copper Orchard", false, true)
{
    Presentation = new("Tree"),
    Production = new DirectionalResourceProduction("fruit", 1,
        Placement: ProductionPlacement.SurroundingCellD8,
        SourceLos: ProductionSourceLos.IgnoreProducingCell)
};
```

New canonical references can be authored as:

```json
{
  "board": {
    "width": 7,
    "height": 3,
    "cells": [
      { "position": { "x": 1, "y": 1 }, "terrainTypeId": "ordinary-lava" },
      { "position": { "x": 3, "y": 1 }, "terrainTypeId": "coal-lava" },
      { "position": { "x": 5, "y": 1 }, "terrainTypeId": "fruit-tree" }
    ]
  }
}
```

A scenario-local producer needs only a definition and a placement:

```json
{
  "terrainTypes": [
    {
      "id": "copper-orchard", "name": "Copper Orchard",
      "passable": false, "blocksLos": true,
      "presentation": { "recipe": "Tree" },
      "production": {
        "kind": "directionalResourceProduction",
        "resourceTypeId": "fruit",
        "attempts": 1,
        "placement": "SurroundingCellD8",
        "sourceLos": "IgnoreProducingCell"
      }
    }
  ],
  "board": {
    "width": 3, "height": 3,
    "cells": [
      { "position": { "x": 1, "y": 1 }, "terrainTypeId": "copper-orchard" }
    ]
  }
}
```

These are fragments to combine with ordinary scenario Units, agency, objectives and Loot settings. The bundled example contains a complete playable definition.

`resourceTypeId` must resolve to an existing Resource Type, and `attempts` must be an integer from 1 through 100. Unknown mechanic kinds, resource IDs, placement policies, source LOS policies and unsupported properties are rejected. `placement` defaults to `SurroundingCellD8`; `sourceLos` defaults to `RespectTerrain`. Spell out both when copying a declaration so its intended rules remain visible. A producer may be passable or impassable and may block LOS or not; these properties remain independent of production. With `RespectTerrain`, an LOS-blocking source cannot satisfy its own production LOS check.

Each attempt rolls exactly once: 1 North, 2 Northeast, 3 East, 4 Southeast, 5 South, 6 Southwest, 7 West, 8 Northwest. It attempts to create one Resource on that Cell. Board bounds, passable destination terrain, Resource-marker permission, no Machine, no existing Resource and geometric LOS all remain authoritative. Units do not prevent placement. The next attempt sees previous successful placements; repeated rolls toward the same Cell cannot create two Resources there. Sources resolve in ascending y, then x, independently of declaration or catalog order.

`sourceLos` is explicit: `RespectTerrain` uses ordinary geometric terrain LOS; `IgnoreProducingCell` exempts only the producing Cell's own terrain from that query. Other terrain, Machines, Edges and corner restrictions still apply. This does not change gameplay LOS, vision or lighting for any other purpose. The existing Coal Lava and Fruit Tree declarations preserve the existing producing-Cell exemption.

Production descriptions come from Engine-owned declarations. Designer and browser presentation consume those descriptions and event metadata rather than maintaining independent production rules. Existing `LavaCoalRolled` / `FruitTreeRolled` events retain their compatibility identities; unfamiliar producers use `TerrainProductionRolled`. Those event identities do not select the production mechanic. Resource creation remains an authoritative state transition. FoW filters sources, definitions and events using the observation at the displayed checkpoint, including replay.

## Compatibility and scope

Legacy enum-based scenario fields remain readable, including implicit Stone Floor, Rail direction and per-Cell Lava Coal settings. Existing scenarios need no rewrite. The Lava adapter preserves serialized meaning:

| Existing reference | Coal setting | Resolved production |
| --- | --- | --- |
| Legacy `terrain: "Lava"` | Omitted or `true` | Coal Lava, two attempts |
| Legacy `terrain: "Lava"` | `false` | Ordinary Lava, no attempts |
| Previous milestone `terrainTypeId: "lava"` | Omitted or `true` | Coal Lava, two attempts |
| Previous milestone `terrainTypeId: "lava"` | `false` | Ordinary Lava, no attempts |
| New `terrainTypeId: "ordinary-lava"` | Not authored | Ordinary Lava, no attempts |
| New `terrainTypeId: "coal-lava"` | Not authored | Coal Lava, two attempts |

Use the explicit new Type IDs for new content. The old `lava` reference is a reserved input alias retained for compatibility instead of silently changing earlier typed scenarios; it is not a third canonical definition and cannot be used as a local definition ID. New explicit `ordinary-lava` and `coal-lava` references reject the legacy `coalGeneration` setting. Legacy default Lava also retains production on every unspecified Cell; Stronghold's explicit `coalGeneration: false` continues to make no rolls. Legacy generator documents and their exact JSON fingerprints remain unchanged.

Resolved runtime catalogs and snapshots retain production data. Runtime Boards write `terrainContentVersion: 2`. An older save with no version is treated as version 1: its former canonical `lava` and Fruit Tree definitions and legacy tile references migrate at the deserialization boundary. A former Lava definition supplies both resolved Lava types; unrelated definitions absent from an old filtered observation remain absent. Normal observations still include only discovered Terrain Types. The scenario format version stays unchanged. Migration does not change remaining Resources, round progress or random state, and never replays production merely because a save is loaded.

- Lava stays impassable. Omitted or `true` Coal generation still produces Coal; `false` still makes no rolls.
- Fruit Tree production, its source-LOS exception and all production ordering remain unchanged.
- Rails retain their required per-Cell direction and concrete Run Rails behavior.
- Water/Wall restrictions and Door/Wall semantics remain unchanged.
- Legacy artwork identities, visual seeds and existing generator serialized output remain stable.

`legacyKind` is compatibility metadata for canonical content, not a way for local definitions to acquire Rail behavior or other specialized mechanics. Selecting a Lava, Fruit Tree or Rail visual recipe does not grant production or movement rules. Production requires a typed declaration.

This milestone does not implement inheritance, Create Variant, Create Copy, nested assemblies, multiple raised furniture objects or producers on one Cell, generic environmental Actions or runtime terrain mutation. Future inheritance and copy operations should remain authoring transformations that resolve to the same immutable Terrain definition boundary. They must not mutate a running game's catalog or turn presentation recipes into game rules.
