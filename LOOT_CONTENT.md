# Loot content

Canonical Loot Cards are declared in `ProjectDelve.Engine/LootRoster.cs`, separately from Items, Equipment and resolution. Each `LootCardDefinition` has a stable ID, a display name and exactly one reward: `itemTypeId`, `equipmentTypeId`, or `immediate`.

[Content Designer](CONTENT_DESIGNER.md) can create scenario-local Loot Cards and
Equipment through forms, with Engine validation and generated previews. New cards
can reference Equipment created in the same draft or existing canonical objects.

An object reward references the existing independent Item or Equipment Type. It does not copy that object's mechanics. Acquisition, duplicate Equipment fallback to the ground, Item consumption and physical Instance IDs continue through the ordinary object lifecycle. The Loot Card ID records provenance; it does not select Item or Equipment behavior. On reveal, an object reward uses the referenced object's name; the Loot Card name labels its authored catalog entry.

## Immediate healing

The supported Immediate mechanic is `healCollector`. It restores the declared amount of HP to the collecting Unit, capped at maximum HP. The draw and healing are mandatory, require no Action or choice, and do not change Posture. The card goes to discard rather than inventory. This is the existing Pocket Breakfast operation, exposed as typed data rather than a card-ID branch.

For example, add these fields to an otherwise complete scenario:

```json
{
  "lootCardDefinitions": [
    {
      "id": "orchard-lunch",
      "name": "Orchard Lunch",
      "immediate": { "kind": "healCollector", "amount": 3 }
    }
  ],
  "loot": {
    "composition": [{ "lootCardId": "orchard-lunch", "count": 2 }]
  }
}
```

The equivalent C# definition is:

```csharp
new LootCardDefinition("orchard-lunch", "Orchard Lunch")
{
    Immediate = new HealCollectorMechanic(Amount: 3)
};
```

Descriptions are generated from the mechanic's amount; there is no independently maintained healing number in presentation prose. Authoring validation accepts healing amounts from 1 through 100 and rejects mixed rewards, unknown mechanics and unresolved object references. Immediate Loot cannot be placed in inventory or authored as a known ground object. Immediate Loot is not an Item Ability or World Card. No effect sequence, script, new timing system or generic Immediate interpreter is supported.

## Dynamic All preset

`All` is a live preset containing exactly one of each Loot Card Type in the
scenario's effective catalog. It includes canonical and scenario-local cards;
new cards participate without changing the preset declaration. Effective authored
IDs use local precedence, so a historical local replacement suppresses the
canonical card with the same ID. Duplicate IDs within one scope still fail
validation. Standalone Equipment and Items without Loot Cards are not included.

Engine resolves the selection in ordinal Type ID order before its existing random
draw path. The scenario retains `preset: "All"`; game setup materializes a detached
composition and physical cards. Saves and replays retain that finite deck and never
rebuild it from a later catalog. Existing fixed presets are unchanged.

`All` accepts no multiplier above one. Stronghold generation caps its initial cache
markers to the available cards, distributing them across its selected caches;
ordinary finite-supply rules still govern subsequent drops. Both generator code
formats accept `QAll`. Designer obtains effective quantities from Engine metadata,
including refreshed local definitions; Copy to Custom stores that resolved selection.

`LootCardCatalog.Effective` and `LootPresets.Resolve` are the shared resolution
boundary. Future library providers can supply resolved definitions there without
adding another card list or dynamic-preset expression system.

## Canonical definitions and compatibility

The canonical IDs are `pocket-breakfast`, `second-wind`, `bedsheet`, `lucky-boots`, `official-key` and `goat-bell`. Pocket Breakfast declares healing amount 2. The other cards reference the existing Item or Equipment of the corresponding ID.

Historical `LootCardKind` input, preset compositions and physical card IDs remain supported. The established presets retain their quantities, ordering and draw behavior. New deck instances record the resolved Loot Card ID and object-reward references, including when the composition uses an old enum. Compatibility maps old enum identities to typed definitions; mechanics resolve from those definitions. New card authoring should use `lootCardId` instead of extending the enum and choose IDs distinct from canonical content.

Older scenarios could use the newly canonical IDs for unrelated local Loot. At game creation, an input adapter gives those local definitions deterministic, unused runtime IDs and updates their explicit Loot references. Legacy enum rows still select canonical rewards. This happens in a resolved copy: the authored document, names, quantities and physical Instance IDs remain unchanged. Designer inspection and export retain the authored local identity, with one explicit-ID choice alongside the historical enum choice.

Second Wind owns a local Bonus Action declaration with `ExtraActionsMechanic(1)`. Item abilities and descriptions resolve from their owned declarations, without a global Ability reference or content-ID handler. Candidate identities select a physical Item copy; accounting uses the local key within its Item Type and acting Unit. The selected physical copy is consumed through the shared Item lifecycle. Inspection does not require every Item to contain an extra-Action mechanic.

Scenario JSON and DELVE1 retain local definitions. Runtime saves carry resolved catalogs. Older saves that explicitly stored an empty or local-only Loot catalog acquire only the missing canonical definitions required by legacy deck objects; existing definitions, draw/discard contents and physical object records are preserved. Historical objects without typed provenance resolve their enum through canonical declarations, even if a saved local definition has the same ID. Explicit saved Loot references still resolve their saved definitions. Observations with no Loot Deck never acquire hidden definitions through this adapter. Browser outcomes resolve observable draws from their authoritative event snapshots; unopened draws and hidden content remain excluded from player observations. Replay uses the content associated with its snapshots.

Unit, Equipment and Item Abilities use complete owner-local declarations and shared typed execution paths; see [Ability content](ABILITY_CONTENT.md). This includes specialized Unit mechanics and Equipment's passive modifiers, Phase and Open Door. Global Ability definitions/grants, native Ability fields and their legacy accounting formats are removed; the Loot adapters above preserve object provenance and deck compatibility only. Content Designer now edits Equipment and Loot Cards. Equipment charges and canonical JSON export remain separate work. Canonical declarations remain C# data, and local definitions use the same typed validation and execution boundary.
