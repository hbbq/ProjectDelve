# World Card content — milestone 3

World Cards are independent definitions with stable Type IDs, display names, an Immediate or Continuous lifecycle, and typed mechanics. Physical cards have separate Instance IDs and refer to those definitions. Deck quantities belong to the scenario's composition, not the definition. World Cards do not use the Item, Equipment or Ability lifecycle.

Canonical content remains declared in [WorldCardRoster.cs](ProjectDelve.Engine/WorldCardRoster.cs). Canonical and scenario-local definitions resolve through `WorldCardCatalog` into the same per-game catalog. Engine resolution and browser presentation use the resolved definition; a new card using a supported mechanic needs no ID- or name-specific branch.

## Playable example

Open **Home → Test Scenarios → Local World Cards - Weather Room → Play scenario**, or use `#scenario/local-world-weather-room`. The complete definition is in [library.json](ProjectDelve.Web/Content/Library/library.json).

The Barbarian and Wizard begin at 1 HP. Start the first round to draw both scenario-local cards, in shuffled order:

- **Paperwind**, Continuous: all Units have MOV +2 and DEF -1. The Barbarian changes from MOV 3 / DEF 3 to MOV 5 / DEF 2; the Wizard changes from MOV 3 / DEF 2 to MOV 5 / DEF 1.
- **Restoring Rain**, Immediate: heal each damaged Unit for 2 HP, capped at maximum HP. Both Heroes reach 3 HP after the first draw of this card.

There are no enemies, hazards or victory condition. Inspect the active World row, Unit Cards and Journal after playback. Later rounds reshuffle Restoring Rain from discard while Paperwind stays active. Restart restores the original damaged setup.

Use **Details → Open in Designer**, then **Test scenario**, and **Menu → Back to Designer**. Imports, exports, recovery documents, library saves and playtests retain the definitions and deck composition. Editing draws per round or Cycling preserves the custom composition. Turning World Effects off removes the enabled deck settings; local definitions remain available in the document. The current Designer does not provide a visual World Card content editor: author definitions and composition in scenario JSON.

## Authoring

`WorldCardDefinition` declares identity, presentation, lifecycle and mechanics. `WorldModifier` values compose additively; an empty modifier list is the existing No Effect Continuous card.

```csharp
var wind = new WorldCardDefinition("paperwind", "Paperwind", WorldCardLifecycle.Continuous)
{
    Modifiers = [new(WorldModifierTarget.Mov, 2), new(WorldModifierTarget.Def, -1)]
};

var scenario = existingScenario with
{
    WorldCardDefinitions = [wind],
    WorldEffects = new(1, 2)
    {
        Composition = [new("paperwind", 2), new("second-wind", 1)]
    }
};
```

The same typed model is JSON. This complete small scenario can be imported through the ordinary scenario JSON/DELVE1 pipeline:

```json
{
  "worldCardDefinitions": [
    {
      "id": "paperwind", "name": "Paperwind", "lifecycle": "Continuous",
      "modifiers": [{ "target": "Mov", "amount": 2 }, { "target": "Def", "amount": -1 }]
    },
    {
      "id": "restoring-rain", "name": "Restoring Rain", "lifecycle": "Immediate",
      "immediate": { "kind": "healDamagedUnits", "amount": 2 }
    }
  ],
  "worldEffects": {
    "cardsPerRound": 2, "cycling": 1,
    "composition": [{ "typeId": "paperwind", "count": 1 }, { "typeId": "restoring-rain", "count": 1 }]
  },
  "board": { "width": 4, "height": 3 },
  "unitTypeIds": ["barbarian-type"],
  "units": [{ "unitTypeId": "barbarian-type", "sideId": "blue", "anchor": { "x": 1, "y": 1 }, "initialHp": 1 }],
  "agency": [{ "unitTypeId": "barbarian-type", "sideId": "blue", "controller": "Human" }],
  "objectives": [{ "sideId": "blue", "objective": "NoObjective" }],
  "loot": { "composition": [] }
}
```

Local IDs must be nonblank and unique, and cannot replace canonical definitions. The same local ID can have different definitions in different scenarios. References must resolve within the current game's catalog. Invalid lifecycle/mechanic combinations, unknown discriminators, unsupported modifier targets and invalid parameters are rejected at the normal game-creation boundary. Current authoring validation accepts modifier amounts from -100 to 100, Immediate amounts from 1 to 100, and one composition row per Type with 1–1000 copies, at most 6000 cards total. Omit a row to include no copies.

## Supported mechanics

Continuous modifiers support `Atk`, `Def`, `Mov`, `Actions` and `BonusActionUses`. Each declares its signed `amount`. Multiple modifiers and active cards add before the existing effective-value floor of zero. These modifiers grant no capability, bypass no ordinary restriction, and do not replenish limited-use counters.

Immediate cards declare one concrete mechanic:

| JSON `kind` | Parameters | Existing operation |
| --- | --- | --- |
| `layDownAllUnits` | None | Lay down all Upright Units. |
| `setAllDoors` | `open`: boolean | Open all Closed Doors or close all Open Doors. |
| `fullyHealUnits` | None | Restore all Units to maximum HP without changing Posture. |
| `healDamagedUnits` | `amount`: positive integer | Heal each damaged Unit, capped at maximum HP. |
| `damageUnitsByAdjacency` | `amount`: positive integer; `adjacent`: boolean | Damage Units adjacent to another Unit, or those not adjacent to another Unit; ignores Side. |
| `replenishLimitedAbilities` | `amount`: positive integer | Restore uses up to each limited-use Ability's maximum; unlimited Abilities are unaffected. |

Effects use the existing Engine operations for healing, damage, Posture, doors and Ability replenishment. Recipient membership, including adjacency, is captured before resolving recipients in anchor-cell board order. Automatic consequences drain completely before drawing the next card. Round Start still resolves World Cards before resource generation and Activation Bag population.

Descriptions are generated from the authoritative mechanics and numeric parameters. `descriptionOverride` allows selective authored wording using `{amount}` for numeric Immediate mechanics or `{modifiers}` for Continuous modifier text. For example, `"Restore {amount} HP to every damaged Unit."` changes with the declared amount. Overrides cannot supply a separate numeric rule value. The browser's card text, event playback and Journal use the same definition and generated description. Designer catalog metadata includes the canonical definitions and their generated descriptions.

## Decks, persistence and compatibility

Omitting `worldEffects` keeps World Effects disabled. Enabled settings with omitted or null `composition` retain the original 40-card canonical deck, in its original pre-shuffle order. Explicit `composition: []` creates an empty deck. Custom rows reference canonical or local Type IDs and supply physical copy counts. Definitions are not automatically added to a deck merely because they exist in the catalog.

Each physical Instance ID remains unchanged through draw, active row and discard. Immediate cards resolve and discard; Continuous cards enter the ordered active row and atomically replace the oldest card when Cycling is exceeded. Equal and opposing modifiers retain their distinct physical cards. Active cards never enter a discard reshuffle. The existing shuffle algorithm and random-consumption order are unchanged.

Legacy `WorldEffect` values have explicit canonical Type-ID mappings. Existing canonical physical IDs and event Action IDs retain their compatibility identities; the enum selects no gameplay handler. Old saves lacking a World Card catalog receive canonical defaults, and old physical cards carrying `effect` remain readable. New runtime saves carry resolved definitions and Type references, including local mechanics, so resume does not depend on an external scenario file changing. Scenario JSON, DELVE1, replay and Designer round-trips preserve the same definitions and composition.

The legacy `WorldCardContent` view includes only Types with historical enum mappings. A mapped definition absent from the baseline composition has zero copies in that view. New canonical definitions require neither a legacy enum entry nor membership in the default deck; typed catalog inspection and explicit compositions use their Type IDs directly.

World Cards are public global content when drawn. Player projections hide the unrevealed draw identities, deck composition and unrevealed definitions; active/resolving cards use authoritative names and descriptions, and already revealed discard cards remain known. Unit consequences still pass through ordinary Fog of War observation filtering.

## Remaining scope

This milestone supports the established World mechanics with parameterized variations and composable Continuous modifiers. It does not add arbitrary Immediate effect sequences, new timing rules, triggers, scripts, runtime content mutation, inheritance or a Content Designer. A new mechanic still requires rules to be established and a concrete Engine implementation exposed through this same typed boundary.
