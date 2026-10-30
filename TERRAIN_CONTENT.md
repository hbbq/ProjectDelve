# Terrain content — milestone 1

Terrain Types have stable Type IDs, display names and declared gameplay properties. Canonical C# and scenario-local JSON definitions resolve through the same per-game catalog. Movement, placement, pathfinding, geometric LOS and Fog of War consume the resolved properties; an unfamiliar Type ID does not need an Engine handler.

This follows the catalog boundary used by [Equipment](EQUIPMENT_CONTENT.md), [Abilities and Items](ABILITY_CONTENT.md) and [World Cards](WORLD_CARD_CONTENT.md). One Cell still contains one terrain tile. A Table rendered on a floor does not become a separately movable or targetable object.

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

var runner = new UnitType("practice-runner", 3, 1, 1, 1, 2)
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

## Compatibility and scope

Legacy enum-based scenario fields remain readable, including implicit Stone Floor, Rail direction and per-Cell Lava Coal settings. Existing scenarios need no rewrite. Canonical legacy identities remain available to mechanics that are intentionally outside this milestone:

- Lava stays impassable. Omitted or `true` Coal generation still produces Coal; `false` still makes no rolls.
- Fruit Tree production, its source-LOS exception and all production ordering remain unchanged.
- Rails retain their required per-Cell direction and concrete Run Rails behavior.
- Water/Wall restrictions and Door/Wall semantics remain unchanged.
- Legacy artwork identities, visual seeds and existing generator serialized output remain stable.

`legacyKind` is compatibility metadata for canonical content, not a way for local definitions to acquire production, Rail behavior or other specialized mechanics. Selecting a Lava or Rail visual recipe does not grant those rules.

This milestone does not implement inheritance, Create Variant, Create Copy, floor/furniture composition, generic environmental Actions, production authoring or runtime terrain mutation. Future authoring transformations should resolve to the same immutable Terrain definition boundary; they must not mutate a running game's catalog or turn presentation recipes into game rules.
