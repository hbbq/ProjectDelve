# Abilities and Single Use Items

Mechanics, Ability definitions, and sources are separate data. A mechanic describes the existing rules operation. An Ability gives it a stable ID, a display name, an explicit timing and usage restriction. A grant attaches that Ability to a Unit Type, Equipment Type or Item Type. The selected source supplies the mechanic; the Engine does not look up a Unit-specific field or dispatch on a display name to perform it.

This extends the Equipment catalog described in [EQUIPMENT_CONTENT.md](EQUIPMENT_CONTENT.md). It does not introduce a second Equipment system, arbitrary scripts, a sequence language or a new rules engine. Milestone 3 adds independent World Card definitions through the same content boundary; see [WORLD_CARD_CONTENT.md](WORLD_CARD_CONTENT.md).

## Playable example

Open **Home → Test Scenarios → Local Abilities - Three Sources → Play scenario**. It is also listed under **Scenarios** and available at `#scenario/local-abilities-three-sources`.

The small board has three separated lanes. Each Hero starts adjacent to a hostile Practice Dummy, which has no movement or Attack. There are no victory conditions or hazards. All three Heroes use the same Holy Wave mechanic:

- The Cleric has its native **Holy Wave**, with its existing two-use limit.
- The Barbarian carries **Velvet Reliquary**, granting the unlimited Action **Curtain Call**.
- The Wizard carries two **Hush Scroll** Items, each granting **Quiet Please**. Using it consumes only the selected Scroll.

For each Hero's first activation, choose **Stay**, then the relevant Action. It lays down the adjacent Upright dummy and the acting Hero. A Lying Hero's next activation only stands it up, as usual. Restart the scenario to compare the same initial situation again. Turn off **Filter irrelevant choices** if you want to use a wave without an Upright enemy, and turn off **Auto-choose single relevant choice** to inspect every ordinary choice manually.

A spare known Hush Scroll lies under the Wizard, available for optional pickup. The unopened marker immediately to the Wizard's right draws a scenario-local Loot Card that references the same Item Type. These examples are authored only in `ProjectDelve.Web/Content/Library/library.json`; none is a permanent canonical Item or Equipment.

## Definitions and grants

The concrete C# records are also the JSON model:

```csharp
var limitedWave = new AbilityDefinition("quiet-wave", "Quiet Wave",
    AbilityTiming.Action, new HolyWaveMechanic())
{
    Usage = new(MaxUses: 2)
};

var portableWave = new AbilityDefinition("curtain-call", "Curtain Call",
    AbilityTiming.Action, new HolyWaveMechanic()); // Unlimited by default.

var equipment = new EquipmentDefinition("velvet-reliquary", "Velvet Reliquary")
{
    Grants = [new("curtain", "curtain-call")]
};

var item = new ItemDefinition("hush-scroll", "Hush Scroll")
{
    Grants = [new("read", "curtain-call")]
};

var unit = new UnitType("quiet-adept", 3, 1, 2, 2, 1)
{
    AbilityGrants = [new("prayer", "quiet-wave")]
};
```

Add these records to a scenario's `AbilityDefinitions`, `EquipmentTypes`, `ItemTypes` and `UnitTypes`, or add permanent definitions to their canonical roster files. An Ability reference resolves within the current game's catalog. A grant's own ID is stable within its source definition and is independent of the referenced Ability ID.

For C# Unit authoring, the equivalent `UnitAuthoring.Grant("prayer", "quiet-wave")` entry adds the same typed grant to `UnitType.AbilityGrants`. Use definitions and grants for the supported mechanics rather than the older `Heal`, `HolyWave`, `Fireball`, Aura or simple `BonusActions` fields. New content normally needs no compatibility metadata.

## Canonical normalization

`AbilityRoster.All` is the canonical catalog for Heal, Holy Wave, Fireball, Aura, Rage, Dash, Throwing Knife, Focus, Emergency Goat Bell's stand-up Action and Second Wind. Each definition declares its name, timing, usage and typed mechanic. Unit, Equipment and Item rosters reference these definitions through the same `AbilityGrant` record.

Cleric, Wizard, Barbarian and Rogue grant their supported Abilities explicitly. Cleric, Royal King, Goblin King and Displacer Demon reference the same Aura definition. Rage, Dash, Throwing Knife and Focus use modifier lists; Throwing Knife does not have a separate implementation. Goat Bell uses `Grants`, while simple Equipment modifiers and capabilities remain their existing direct typed declarations.

The remaining specialized Unit declarations, including Cleave, Fury, Backstab, Fire Breath, Summoning and Telekinesis, retain their concrete representations. Their mechanics are not yet part of the shared Ability model. Normalizing the supported subset does not introduce new mechanic/timing combinations.

Canonical declarations live separately from mechanic implementations and catalog validation:

| Content | Authoring file |
| --- | --- |
| Units | [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs) |
| Abilities | [AbilityRoster.cs](ProjectDelve.Engine/AbilityRoster.cs) |
| Single Use Items | [ItemRoster.cs](ProjectDelve.Engine/ItemRoster.cs) |
| Equipment | [EquipmentRoster.cs](ProjectDelve.Engine/EquipmentRoster.cs) |
| Loot Cards and presets | [LootRoster.cs](ProjectDelve.Engine/LootRoster.cs) |
| Existing World Cards | [WorldCardRoster.cs](ProjectDelve.Engine/WorldCardRoster.cs) |

The catalogs expose these rosters through their existing APIs and retain validation and scenario-local resolution. `LootRoster.Definitions` declares all canonical Loot Cards, including typed Immediate healing and independent Item/Equipment rewards; see [Loot content](LOOT_CONTENT.md). Legacy card metadata is a compatibility projection of these definitions and their referenced objects. World Cards use the independent typed definitions documented in milestone 3. A future JSON provider can replace a roster at this boundary while producing the same typed definitions and passing the same validation.

Supported modifier lists remain ordinary data:

```csharp
new AbilityDefinition("careful-throw", "Careful Throw",
    AbilityTiming.BonusAction,
    new SelfModifiersMechanic([new(Stat.Rng, 2), new(Stat.Atk, -1)]))
{
    Usage = new(MaxUses: 2)
};

new AbilityDefinition("protective-presence", "Protective Presence",
    AbilityTiming.Passive,
    new AdjacentModifiersMechanic([new(Stat.Def, 1)]));
```

Self modifiers used as a Bonus Action last through the current activation using the existing temporary-modifier lifecycle. Adjacent modifiers affect other adjacent friendly Units while the source is Upright, using ordinary authoritative adjacency. Numeric amounts belong in these declarations, not in independently maintained description strings.

## Scenario-local JSON

These fields can be added to an ordinary scenario definition:

```json
{
  "abilityDefinitions": [
    {
      "id": "curtain-call",
      "name": "Curtain Call",
      "timing": "Action",
      "mechanic": { "kind": "holyWave" }
    }
  ],
  "equipmentTypes": [
    {
      "id": "velvet-reliquary",
      "name": "Velvet Reliquary",
      "grants": [{ "id": "curtain", "abilityId": "curtain-call" }]
    }
  ],
  "itemTypes": [
    {
      "id": "hush-scroll",
      "name": "Hush Scroll",
      "grants": [{ "id": "read", "abilityId": "curtain-call" }]
    }
  ],
  "lootCardDefinitions": [
    {
      "id": "sealed-scroll",
      "name": "A Sealed Scroll",
      "itemTypeId": "hush-scroll"
    }
  ],
  "loot": {
    "composition": [{ "lootCardId": "sealed-scroll", "count": 2 }]
  }
}
```

An individual inventory or known ground object references the Item directly:

```json
{ "id": "scroll-instance-17", "itemTypeId": "hush-scroll" }
```

The physical Instance ID stays with the object through deck, inventory, ground and discard. Multiple copies of one Item Type are legal. Using one removes that particular object from inventory and places it in discard; it does not consume another copy or multiply the effect. Equipment retains the one-instance-per-Type ownership rule from milestone 1.

A local Loot Card's name labels the authored card in the content catalog. On reveal, gameplay presents the referenced Item or Equipment's authoritative name, matching its inventory presentation. `lootCardId` preserves the source card as provenance; if present on a physical object, it must resolve to a card referencing that object's Type. Item mechanics never depend on that provenance.

A new local Unit uses `abilityGrants` with the same `{ "id", "abilityId" }` shape. Existing concrete scenario-local Unit Types and Equipment definitions remain supported. Local catalogs are merged into the individual scenario/game context; they do not mutate process-global canonical definitions or another scenario. Duplicate definitions, unresolved references, invalid grant IDs and unsupported mechanic/timing/source combinations are rejected before play.

## Identity and accounting

Four identities have distinct purposes:

| Identity | Example | Purpose |
| --- | --- | --- |
| Ability definition | `curtain-call` | Resolves name, timing, restriction and mechanic. |
| Grant | `curtain` | Identifies the attached Ability within a source definition. |
| Physical source instance | `reliquary-instance-17` | Identifies the object being used or consumed. |
| Accounting | Source and grant-derived key | Tracks the relevant Unit's use allowance without relying on names or mechanic equality. |

Two grants may use the same mechanic or even reference the same Ability definition without sharing native limited-use counters. Item copies of the same Type and grant share their per-activation Bonus Action allowance: a second physical copy is not a new allowance. Distinct source grants remain distinct. All ordinary Actions still spend the same activation's Action budget, and World effects that change the Action or Bonus Action allowance continue to apply.

Limited-use counters belong to native Unit grants. Equipment and Item grants currently require unlimited Ability usage. Equipment charges remain deferred; an Item's single-use lifecycle is physical consumption, not a refillable one-use Ability. Renewal may restore a native limited-use Ability, but cannot recreate a consumed Item.

Usage is recorded and an Item is consumed when its legal choice is applied, before its mechanic resolves. Mechanics preserve their existing recipient, targeting, Posture, consequence and Attack boundaries. Holy Wave is legal without adjacent Upright enemies, and still lays down its acting Unit. Lying Units cannot invoke ordinary granted abilities; continuous grants supplied by Lying sources are inactive.

## Compatibility and presentation

Normalized canonical grants carry explicit `Compatibility` metadata where historical identities must remain stable. `AbilityGrantCompatibility` records only the old identity/storage kind, an optional raw key and an Equipment candidate prefix. It supplies no mechanic, name, timing, amount or use limit. The referenced Ability definition remains authoritative, even when its name or source changes. Ordinary new grants omit this metadata.

Native Holy Wave, Heal and Fireball retain their historical candidate and Action IDs and dedicated serialized counters. Simple native Bonus Action modifiers retain their raw keys (`Rage`, `Dash`, `Throwing Knife`, `Focus`), per-activation accounting and historical events. Aura retains its `passive:Aura` card entry. Goat Bell retains `goat-bell:<target>` choices, the `goat-bell` Action ID and its Equipment card entry. Initial counter maxima come from the resolved definition's usage restriction; initialization never replenishes an existing spent counter.

New grants use source/grant accounting rather than display names or mechanic types. If a source-derived key collides with a legacy raw Bonus Action key, including a normalized compatibility-bound grant, it receives a deterministic `grant:` prefix until distinct. Physical copies of the same Item grant still share one allowance. Multiple grants using one mechanic remain independent; two compatibility bindings cannot claim the same historical counter or entry.

`AbilityQueries.LegacyNative` still adapts old concrete Unit fields, and the inline `EquipmentAbility` adapter still reads older Equipment definitions. These paths preserve saved authored values instead of replacing them with today's canonical roster. Do not combine a concrete legacy field and a normalized grant for the same historical slot: validation rejects the duplicate. Compatibility is isolated in `AbilityCompatibility` and `AbilityCatalogCompatibility`; it does not select the executed mechanic.

Before these canonical Ability IDs existed, scenarios could use them for local definitions. Game creation now aliases those local definitions and their explicit Unit, Equipment and Item references in a resolved copy. The original scenario document and grant IDs remain unchanged. For old saves that omit the Equipment catalog but contain an older Ability catalog, the adapter supplies missing default-Equipment dependencies, or gives them distinct IDs when a saved local definition already occupies that ID. Existing saved definitions and explicit Equipment catalogs remain authoritative.

Legacy Second Wind objects still load and use the independent `second-wind` Item definition. Multiple copies still share the established Bonus Action allowance and each use grants the authored number of additional Actions while consuming one instance. Existing Loot presets and object Instance IDs are preserved. Milestone 1 Equipment declarations remain readable through compatibility adapters.

Fireball attack-position planning resolves available Ability sources, including native grants, Equipment and Items. Exhausting one source does not exhaust another. Automated attack tie-breaking reads the selected Ability's usage restriction rather than assuming every Fireball is limited. Item consumption remains independent of an Ability's unlimited usage; this adds no new consumable-conservation policy. Target geometry, Fog of War and ordinary Action availability remain authoritative.

An old save that already shared one raw accounting key between a custom Bonus Action and Second Wind cannot identify which source spent that count. Resume that unusual case at a fresh activation; ordinary legacy saves and all new source counters retain their distinct accounting.

Scenario JSON, DELVE1 transport, Designer import/export and playtest creation preserve the local catalogs. Runtime saves contain resolved definitions as well as source identities and counters. Pending decisions are rebuilt from authoritative state on resume. Existing supported saves that omit the new catalogs receive the canonical defaults; dedicated legacy counters and physical object IDs remain readable. No new old-save format incompatibility is introduced by this milestone.

Descriptions, timing categories, use limits, effective parameters and source labels are projected from definitions. State-aware Unit and Equipment projections receive the state's Ability catalog, so saved and local definitions supply their own names and parameters. Historical card ordering and short descriptions are retained. `DescriptionOverride` allows selective authored wording; it does not alter execution or usage. A parameterized override must include `{amount}` or `{modifiers}`, as appropriate, and cannot duplicate numeric literals. For example, a Heal description may say `Restore {amount} HP to an adjacent damaged friendly Unit.` Players receive only observable content and source information under Fog of War. Designer remains omniscient. The existing Unit Card and acquired-ability grouping are retained.

Derived `UnitType.Passives` and the generated Fury/Backstab name/text properties remain available to C# callers but are no longer serialized as redundant content. Historical JSON containing those fields remains readable. Typed definitions and grants round-trip through the existing JSON model; this is not a full canonical catalog export/load feature.

## Supported scope and remaining work

The shared model supports the migrated concrete mechanics, not arbitrary combinations of every engine operation. Validation permits only their established timing/source combinations. Holy Wave, Heal, Fireball and standing an adjacent friendly Lying Unit use concrete Action implementations. Simple self and adjacent modifiers retain parameter lists; additional Actions implement Second Wind's existing Bonus Action effect.

| Mechanic discriminator | Supported timing | Sources |
| --- | --- | --- |
| `holyWave`, `heal`, `fireball`, `standAdjacentFriendly` | Action | Unit, Equipment, Item |
| `selfModifiers` | BonusAction | Unit, Equipment, Item |
| `selfModifiers`, `adjacentModifiers` | Passive | Unit, Equipment |
| `extraActions` | BonusAction | Unit, Equipment, Item |

Passive abilities must be unlimited. Equipment and Item grants must also be unlimited; limited-use native grants may use the supported active timings. The shared Ability model does not yet accept Free Action or trigger mechanics. Existing Equipment Phase and Open Door declarations continue to use milestone 1's typed capabilities.

Specialized mechanics that have not migrated, including Cleave and other follow-ups or triggers, retain their existing implementation and serialization. No arbitrary trigger timing, scripting, inheritance system, Equipment charges, trading, crafting, slots, runtime content editor, full Content Designer or canonical JSON catalog migration is introduced here. Further mechanics can migrate one concrete operation at a time through the same definition/grant boundary.
