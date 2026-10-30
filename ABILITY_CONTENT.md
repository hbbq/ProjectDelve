# Abilities and Single Use Items — milestone 2

Mechanics, Ability definitions, and sources are separate data. A mechanic describes the existing rules operation. An Ability gives it a stable ID, a display name, an explicit timing and usage restriction. A grant attaches that Ability to a Unit Type, Equipment Type or Item Type. The selected source supplies the mechanic; the Engine does not look up a Unit-specific field or dispatch on a display name to perform it.

This extends the Equipment catalog described in [EQUIPMENT_CONTENT.md](EQUIPMENT_CONTENT.md). It does not introduce a second Equipment system, arbitrary scripts, a sequence language or a new rules engine.

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

Canonical declarations live separately from mechanic implementations and catalog validation:

| Content | Authoring file |
| --- | --- |
| Units | [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs) |
| Abilities | [AbilityRoster.cs](ProjectDelve.Engine/AbilityRoster.cs) |
| Single Use Items | [ItemRoster.cs](ProjectDelve.Engine/ItemRoster.cs) |
| Equipment | [EquipmentRoster.cs](ProjectDelve.Engine/EquipmentRoster.cs) |
| Loot Cards and presets | [LootRoster.cs](ProjectDelve.Engine/LootRoster.cs) |
| Existing World Cards | [WorldCardRoster.cs](ProjectDelve.Engine/WorldCardRoster.cs) |

The catalogs expose these rosters through their existing APIs and retain validation and scenario-local resolution. Loot's legacy card metadata is projected from the independent Item and Equipment definitions; `LootRoster.Definitions` is the declaration list for independent canonical object-reward cards. The World Card extraction only separates existing declarations into a file; its mechanics and format have not migrated. A future JSON provider can replace a roster at this boundary while producing the same typed definitions and passing the same validation. No content loader or format migration is needed for this file separation.

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

Existing canonical content is adapted into the shared typed model. Native Holy Wave, Heal and Fireball keep their legacy candidate and Action IDs and dedicated serialized counters where required for existing saves. Simple native Bonus Action modifiers retain their historical stable accounting keys; the old `Name` field already served as that identity and its display override remains separate. New grants use source/grant accounting rather than display names or mechanic types. If a source-derived key collides with a legacy raw Bonus Action key, it receives a deterministic `grant:` prefix until distinct; the legacy key and candidate IDs remain unchanged, and physical copies of the same Item grant still share one allowance.

Legacy Second Wind objects still load and use the independent `second-wind` Item definition. Multiple copies still share the established Bonus Action allowance and each use grants the authored number of additional Actions while consuming one instance. Existing Loot presets and object Instance IDs are preserved. Milestone 1 Equipment declarations remain readable through compatibility adapters.

An old save that already shared one raw accounting key between a custom Bonus Action and Second Wind cannot identify which source spent that count. Resume that unusual case at a fresh activation; ordinary legacy saves and all new source counters retain their distinct accounting.

Scenario JSON, DELVE1 transport, Designer import/export and playtest creation preserve the local catalogs. Runtime saves contain resolved definitions as well as source identities and counters. Pending decisions are rebuilt from authoritative state on resume. Existing supported saves that omit the new catalogs receive the canonical defaults; dedicated legacy counters and physical object IDs remain readable. No new old-save format incompatibility is introduced by this milestone.

Descriptions, timing categories, use limits, effective parameters and source labels are projected from definitions. `DescriptionOverride` allows selective authored wording; it does not alter execution or usage. A parameterized override must include `{amount}` or `{modifiers}`, as appropriate, and cannot duplicate numeric literals. For example, a Heal description may say `Restore {amount} HP to an adjacent damaged friendly Unit.` Players receive only observable content and source information under Fog of War. Designer remains omniscient. The existing Unit Card and acquired-ability grouping are retained.

## Supported scope and remaining work

The shared model supports the migrated concrete mechanics, not arbitrary combinations of every engine operation. Validation permits only their established timing/source combinations. Holy Wave, Heal, Fireball and standing an adjacent friendly Lying Unit use concrete Action implementations. Simple self and adjacent modifiers retain parameter lists; additional Actions implement Second Wind's existing Bonus Action effect.

| Mechanic discriminator | Supported timing | Sources |
| --- | --- | --- |
| `holyWave`, `heal`, `fireball`, `standAdjacentFriendly` | Action | Unit, Equipment, Item |
| `selfModifiers` | BonusAction | Unit, Equipment, Item |
| `selfModifiers`, `adjacentModifiers` | Passive | Unit, Equipment |
| `extraActions` | BonusAction | Unit, Equipment, Item |

Passive abilities must be unlimited. Equipment and Item grants must also be unlimited; limited-use native grants may use the supported active timings. The shared Ability model does not yet accept Free Action or trigger mechanics. Existing Equipment Phase and Open Door declarations continue to use milestone 1's typed capabilities.

Specialized mechanics that have not migrated, including Cleave and other follow-ups or triggers, retain their existing implementation and serialization. No arbitrary trigger timing, scripting, inheritance system, Equipment charges, trading, crafting, slots, runtime content editor, World Card content migration or full Content Designer is introduced here. Further mechanics can migrate one concrete operation at a time through the same definition/grant boundary.
