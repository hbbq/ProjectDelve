# Equipment content — milestone 1

Milestone 2 extends this model with shared Ability grants and independent Single Use Items. See [ABILITY_CONTENT.md](ABILITY_CONTENT.md) for new Equipment Actions that reuse Unit mechanics, supported source/timing combinations and scenario-local Item authoring. Milestone 3 adds independent [World Card content](WORLD_CARD_CONTENT.md). The milestone 1 declarations below remain compatible.

## Playable example: Local Equipment - Fitting Room

Open **Home → Test Scenarios → Local Equipment - Fitting Room → Play scenario** (or find the same title under **Scenarios**). Its complete definition lives in `ProjectDelve.Web/Content/Library/library.json`; Paperwind Cape is local test content, not a canonical Equipment Type.

Inspect the Barbarian before starting: Paperwind Cape gives **MOV 3 → 4** and **DEF 3 → 2**. The Wizard starts unchanged at **MOV 3 / DEF 2**, standing on a second known copy. Start round; on the Wizard's activation, collect the Cape as a Free Action to get **MOV 4 / DEF 1**. There are no enemies, hazards, random Loot or victory condition. Disable **Auto-choose single relevant choice** in Settings if you want to select pickup manually. Do not use temporary Bonus Actions while comparing the base and Equipment stats.

Use the scenario's **Details → Open in Designer**, then **Test scenario** and **Menu → Back to Designer** to edit and play the same workspace. **Save to library** saves the initial setup, including local definitions and starting ownership. **Restart current** restores that setup: the Barbarian owns the first copy and the second returns to the ground under the Wizard. Restart is a fresh game, not a resume of an in-game pickup.

Equipment is independent content. A definition has a stable Type ID, a display name and typed declarations of supported mechanics. An owned or ground object has its own physical Instance ID and references the Equipment Type. Loot supplies instances of those same Types; an inventory or known ground object needs no Loot Deck.

The engine resolves canonical definitions and a scenario's local definitions into one catalog. All Equipment queries use that catalog. Names and IDs identify content and its source; they do not select gameplay handlers. Runtime ownership, position, remaining draw cards and Action accounting remain in game state, separate from definitions.

## Adding canonical Equipment

Add an `EquipmentDefinition` to `EquipmentRoster.All` in [EquipmentRoster.cs](ProjectDelve.Engine/EquipmentRoster.cs) using supported declarations. `EquipmentCatalog` retains validation and scenario-local resolution separately from the authored content. For example:

```csharp
new EquipmentDefinition("trail-medallion", "Trail Medallion")
{
    Modifiers = [new(Stat.Mov, 1), new(Stat.Def, 1)],
    Capabilities = [EquipmentCapability.Phase]
}
```

`Modifiers` supports MOV, RNG, ATK and DEF through `Stat`; each declaration owns its numeric amount. `Capabilities` supports Phase and Open Door. Use `LocalAbilities` for complete owned Actions and other supported Abilities. Canonical Emergency Goat Bell owns an unlimited Action with `StandAdjacentFriendlyMechanic`. It spends an ordinary Action and stands one adjacent friendly Lying Unit. An unlimited usage restriction does not grant extra Actions. Existing `Grants` references remain supported for compatibility and intentional sharing.

For example, a second Equipment source can grant that same mechanic with its own name:

```csharp
new EquipmentDefinition("holy-bell", "Holy Bell")
{
    LocalAbilities = [new("ring", "Stand Up", AbilityTiming.Action,
        new StandAdjacentFriendlyMechanic()) { Usage = new(MaxUses: null) }]
}
```

Use `DescriptionOverride` on an Ability definition only when the generated wording needs an authored presentation override. It does not alter the mechanic, targets, category or usage. Stat descriptions always come from the typed amounts. Canonical Goat Bell's local declaration has explicit compatibility metadata preserving existing saved decision keys; leave it unset for new content, whose keys derive from source Instance ID and local Ability key. The older inline `Abilities`/`EquipmentAbility` representation remains a reader for historical scenarios and saved catalogs.

These are ordinary serializable C# definitions. JSON produces the same model and passes the same validation. No delegate, script, parsed Ability prose, content-specific Engine handler or Web switch is required. Adding a genuinely new mechanic still requires a rules decision and implementation; this milestone deliberately supports only the established mechanics above.

Canonical Ability, Item, Loot and existing World Card declarations likewise live in `AbilityRoster.cs`, `ItemRoster.cs`, `LootRoster.cs` and `WorldCardRoster.cs`, separate from their mechanics. See [Ability content](ABILITY_CONTENT.md) for the authoring model and roster responsibilities. A future JSON provider can replace these roster providers at the same typed catalog boundary; the current extraction changes no IDs or serialized formats.

Give an instance directly with `new OwnedObject("physical-medallion-1", "trail-medallion")`. Keep its Instance ID unchanged when moving it between inventory and ground. To offer it as random Loot, add `new LootCardQuantity("trail-medallion", 2)` to a Custom Loot composition. Canonical presets continue to reference their existing centrally maintained cards.

## Scenario-local JSON

The following complete small scenario declares an unfamiliar Equipment Type, gives one instance to a custom Unit, places another on the ground, and makes two available through Loot. An explicit `equipmentTypeId` identifies Equipment independently of the legacy `kind` field, which can be omitted.

```json
{
  "equipmentTypes": [
    {
      "id": "amber-sandals",
      "name": "Sandals of a Very Different Name",
      "modifiers": [
        { "stat": "Mov", "amount": 2 },
        { "stat": "Def", "amount": 1 }
      ],
      "capabilities": ["Phase"]
    }
  ],
  "loot": {
    "composition": [
      { "count": 2, "equipmentTypeId": "amber-sandals" }
    ]
  },
  "board": { "width": 4, "height": 3 },
  "unitTypeIds": ["local-collector"],
  "unitTypes": [
    {
      "id": "local-collector",
      "mov": 3, "rng": 1, "atk": 1, "def": 0, "hp": 2,
      "canHaveEquipment": true, "unique": true
    }
  ],
  "units": [
    {
      "unitTypeId": "local-collector", "sideId": "blue",
      "anchor": { "x": 0, "y": 0 }, "unitId": "bearer",
      "inventory": [
        { "id": "owned-17", "equipmentTypeId": "amber-sandals" }
      ]
    }
  ],
  "agency": [
    { "unitTypeId": "local-collector", "sideId": "blue", "controller": "Human" }
  ],
  "groundObjects": [
    {
      "object": { "id": "ground-23", "equipmentTypeId": "amber-sandals" },
      "position": { "x": 2, "y": 0 }
    }
  ]
}
```

Local IDs must be unique and cannot replace a canonical Equipment Type. The same local ID may mean different content in different scenarios; catalogs are never registered globally. All references must resolve within the current scenario's catalog. Local Unit Type definitions continue to work alongside local Equipment definitions.

Scenario JSON and DELVE1 transport carry local definitions. Designer imports, exports and Playtest creation preserve those definitions. Runtime save data includes the resolved Equipment catalog and physical instances, so save/resume does not lose local mechanics or resolve a different preset.

## Ownership and compatibility

A Unit can own one instance of each Equipment Type and any number of different Types. Stat modifiers from different Types add independently, including equal modifiers. Two copies of one Type can belong to different Units or lie on the ground. Automatic Loot acquisition puts a duplicate Type on the ground; optional pickup does not offer it to a Unit that already owns that Type. Effects belong to the individual bearer, including individual members of one Unit group.

Single Use Items retain their existing behavior: multiple copies are permitted, one use consumes one physical instance, and ordinary Action and Bonus Action limits still apply. This is not an Item migration.

Legacy `LootCardKind` Equipment values remain readable: `Bedsheet` maps to `bedsheet`, `LuckyBoots` to `lucky-boots`, `OfficialKey` to `official-key`, and `GoatBell` to `goat-bell`. Existing Instance IDs, preset compositions and candidate compatibility are preserved. New content should reference Equipment Type IDs directly. When loading legacy inventories with repeated card-linked Equipment Types, keep the first instance and place excess legacy instances on the ground at the owner's anchor. This explicit compatibility migration preserves every physical Instance ID while applying the new ownership rule. Newly authored duplicate Equipment Type references are rejected. The engine never destroys or combines physical objects to resolve duplicates.

Mechanical descriptions and inventory metadata come from the resolved definitions. Numeric parameters remain authoritative in typed declarations. Equipment names and Ability names are presentation data. Fog of War must reveal only observed objects and their content, never unopened draw identities or hidden inventories.

## Next milestone

This slice does not migrate Unit Actions, Single Use Items or World Cards to source-independent Ability definitions. It adds no Equipment slots, equip/unequip actions, per-instance charges, generic effect interpreter, trigger language or scripting support. Later work can generalize the shared definition/validation boundary and separate mechanic, source, name and usage fields when concrete content requires it. Runtime content editing and broader content libraries remain future work.
