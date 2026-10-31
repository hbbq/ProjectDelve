# Equipment content

Equipment owns complete local Ability declarations, using the same typed model as Units and Single Use Items. See [ABILITY_CONTENT.md](ABILITY_CONTENT.md) for the shared model and [MECHANIC_CATALOG.md](MECHANIC_CATALOG.md) for supported source, timing and usage combinations. [World Card content](WORLD_CARD_CONTENT.md) and [Loot content](LOOT_CONTENT.md) have their own typed definitions. [Content Designer](CONTENT_DESIGNER.md) provides form-based scenario-local Equipment and Loot authoring using these same declarations.

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
    LocalAbilities =
    [
        new("stride", "Steady Stride", AbilityTiming.Passive,
            new SelfModifiersMechanic([new(Stat.Mov, 1), new(Stat.Def, 1)])),
        new("phase", "Phase", AbilityTiming.Passive, new Phase())
    ]
}
```

`LocalAbilities` is the sole Equipment mechanic declaration list. Passive `SelfModifiersMechanic` supports MOV, RNG, ATK and DEF through `Stat`; each declaration owns its numeric amounts. `Phase` uses Passive timing; `OpenDoor` supports Action or Free Action timing. These abilities are active only while the bearer is Upright. Canonical Emergency Goat Bell owns an unlimited Action with `StandAdjacentFriendlyMechanic`. It spends an ordinary Action and stands one adjacent friendly Lying Unit. Unlimited usage does not grant extra Actions. Supported active Equipment Abilities may instead declare a positive `usage.maxUses`.

For example, a second Equipment source can grant that same mechanic with its own name:

```csharp
new EquipmentDefinition("holy-bell", "Holy Bell")
{
    LocalAbilities = [new("ring", "Stand Up", AbilityTiming.Action,
        new StandAdjacentFriendlyMechanic()) { Usage = new(MaxUses: null) }]
}
```

Each local declaration owns an explicit key, name, timing, usage and typed mechanic. Keys must be unique within the Equipment Type; different owners can reuse a key. Keep the key stable when renaming or reordering an Ability. Candidate and accounting identities bind the local key to the physical Equipment Instance ID; card entries bind it to the Equipment Type. Open Door choices retain their individual source binding even when several owned sources can open the same Door.

Spent uses are stored on the physical Equipment instance by local Ability key. Defeat and later pickup preserve its remaining uses, including zero. Separate objects and separate local keys have independent counts. Renewal replenishes limited Abilities of currently owned Equipment on board Units, up to each declared maximum; Equipment on the ground is unaffected. Use counters are visible with the physical object, including in ground inspection. This does not add voluntary transfer or trading actions.

Use `DescriptionOverride` only when generated wording needs an authored presentation override. It does not alter the mechanic, targets, category or usage. Generated stat descriptions use the typed amounts. The old top-level `Modifiers`, `Capabilities`, inline `Abilities`, and global `Grants` formats are removed; author them as local Abilities instead. Removed Ability formats and their old decision/counter identities are not save compatibility contracts.

These are ordinary serializable C# definitions. JSON produces the same model and passes the same validation. No delegate, script, parsed Ability prose, content-specific Engine handler or Web switch is required. Adding a genuinely new mechanic still requires a rules decision and implementation. The Mechanic Catalog is authoritative for which existing mechanics support Equipment.

Canonical Units, Items, Loot and World Cards likewise live in `UnitRoster.cs`, `ItemRoster.cs`, `LootRoster.cs` and `WorldCardRoster.cs`, separate from their mechanics. Abilities are nested in their owners; there is no global Ability roster or registration. Canonical declarations remain C# data and local definitions use the same typed model. A future JSON provider can use this catalog boundary without introducing another execution path.

Give an instance directly with `new OwnedObject("physical-medallion-1", "trail-medallion")`. Keep its Instance ID unchanged when moving it between inventory and ground. To offer it as random Loot, add `new LootCardQuantity("trail-medallion", 2)` to a Custom Loot composition. Canonical presets continue to reference their existing centrally maintained cards.

## Scenario-local JSON

The following complete small scenario declares an unfamiliar Equipment Type, gives one instance to a custom Unit, places another on the ground, and makes two available through Loot. An explicit `equipmentTypeId` identifies Equipment independently of the legacy `kind` field, which can be omitted.

```json
{
  "equipmentTypes": [
    {
      "id": "amber-sandals",
      "name": "Sandals of a Very Different Name",
      "localAbilities": [
        { "key": "stride", "name": "Steady Stride", "timing": "Passive",
          "mechanic": { "kind": "selfModifiers", "modifiers": [
            { "stat": "Mov", "amount": 2 }, { "stat": "Def", "amount": 1 }
          ] } },
        { "key": "phase", "name": "Phase", "timing": "Passive",
          "mechanic": { "kind": "phase" } }
      ]
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

Single Use Items use the same owned Ability model with their supported Action and Bonus Action profiles. Multiple copies are permitted, one use consumes one physical instance, and ordinary Action and Bonus Action limits still apply.

Legacy `LootCardKind` Equipment values remain readable: `Bedsheet` maps to `bedsheet`, `LuckyBoots` to `lucky-boots`, `OfficialKey` to `official-key`, and `GoatBell` to `goat-bell`. Existing physical Instance IDs and preset compositions are preserved. This object-provenance compatibility is separate from the removed Ability formats. New content should reference Equipment Type IDs directly. When loading legacy inventories with repeated card-linked Equipment Types, keep the first instance and place excess legacy instances on the ground at the owner's anchor. This explicit compatibility migration preserves every physical Instance ID while applying the ownership rule. Newly authored duplicate Equipment Type references are rejected. The engine never destroys or combines physical objects to resolve duplicates.

Mechanical descriptions and inventory metadata come from the resolved definitions. Numeric parameters remain authoritative in typed declarations. Equipment names and Ability names are presentation data. Fog of War must reveal only observed objects and their content, never unopened draw identities or hidden inventories.

## Scope

The model adds no Equipment slots, equip/unequip actions, generic effect interpreter, trigger language or scripting support. Specialized mechanics without source adapters remain restricted by their catalog profiles. Content Designer supports scenario-local Equipment authoring, including the Engine's supported timing and use-limit combinations; runtime content editing and canonical JSON export remain future work.
