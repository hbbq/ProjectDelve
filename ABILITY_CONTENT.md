# Owner-local Abilities

Units, Equipment and Single Use Items own their complete Ability declarations. Each declaration contains a stable owner-local key, display name, timing, usage restriction and typed mechanic payload, with an optional description override. `LocalAbilityDefinition` is the C# and JSON model; there is no global Ability catalog, grant reference or legacy native Ability representation.

The [Mechanic Catalog](MECHANIC_CATALOG.md) registers reusable Engine operations and their supported contexts. It owns parameter constraints, validation and description metadata. It does not own authored Abilities or runtime state. Names and local keys never select an implementation.

[Content Designer](CONTENT_DESIGNER.md) uses those contracts to edit Equipment-owned
Abilities through forms. Modifiers and capabilities remain ordinary local Abilities;
there is no separate editor gameplay model.

## Authoring

Canonical content lives in [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs), [EquipmentRoster.cs](ProjectDelve.Engine/EquipmentRoster.cs) and [ItemRoster.cs](ProjectDelve.Engine/ItemRoster.cs). A declaration in any of these owners produces the same typed data as scenario-local JSON. Small C# helpers may construct or copy declarations, but no second definition is registered or consulted at runtime.

```csharp
var medic = new UnitType("field-medic", 3, 1, 1, 1, 1)
{
    LocalAbilities =
    [
        new("mend", "Heal", AbilityTiming.Action, new HealMechanic(Amount: 2))
            { Usage = new(MaxUses: 2) },
        new("ward", "Aura", AbilityTiming.Passive,
            new AdjacentModifiersMechanic([new(Stat.Def, 1)]))
    ]
};
```

The equivalent concise roster entries are:

```csharp
Ability("Heal", Uses(2), Heal(amount: 2), id: "heal"),
Ability("Holy Wave", Uses(2), HolyWave(), id: "holy-wave"),
Ability("Aura", Unlimited(), AdjacentFriendliesDefenceBonus(amount: +1), id: "aura")
```

`UnitAuthoring.Ability` requires an explicit local `id`; its concrete helpers select their established timing. Use `LocalAbility(new(...))` when specifying timing directly, especially for shared mechanics supporting both Bonus Action and Passive contexts. Helpers only construct data. `UnitType.Define` validates the resulting declarations.

Equivalent scenario JSON:

```json
{
  "unitTypes": [{
    "id": "field-medic", "mov": 3, "rng": 1, "atk": 1, "def": 1, "hp": 1,
    "localAbilities": [
      { "key": "mend", "name": "Heal", "timing": "Action",
        "usage": { "maxUses": 2 }, "mechanic": { "kind": "heal", "amount": 2 } },
      { "key": "ward", "name": "Aura", "timing": "Passive",
        "mechanic": { "kind": "adjacentModifiers", "modifiers": [{ "stat": "Def", "amount": 1 }] } }
    ]
  }],
  "equipmentTypes": [{
    "id": "quiet-lantern", "name": "Quiet Lantern",
    "localAbilities": [{ "key": "use", "name": "Curtain Call", "timing": "Action",
      "mechanic": { "kind": "holyWave" } }]
  }],
  "itemTypes": [{
    "id": "paper-lantern", "name": "Paper Lantern",
    "localAbilities": [{ "key": "use", "name": "Quiet Please", "timing": "Action",
      "mechanic": { "kind": "holyWave" } }]
  }]
}
```

These are catalog fragments; ordinary placement, controller and inventory fields select the owners and physical objects. An inventory object references its Type independently, for example `{ "id": "scroll-17", "itemTypeId": "paper-lantern" }`. Loot Cards reference Item and Equipment Types; they do not duplicate their mechanics. See [Loot content](LOOT_CONTENT.md) and [Equipment content](EQUIPMENT_CONTENT.md).

Keys are case-sensitive, nonblank and unique within an owner. Different owners may use the same key. Keep keys stable when renaming, reordering or changing parameters; the Engine never derives a key from a name or mechanic. Omitted `usage` means unlimited. Limited usage requires a positive `maxUses` and a supported mechanic/context profile.

## Runtime identity and accounting

Resolution binds each local declaration to its source through `ResolvedAbility`. The declaration is authoritative; this binding supplies physical identity and presentation context, not another catalog entry.

| Scope | Accounting identity |
| --- | --- |
| Unit limited uses | Unit instance plus local Ability key |
| Equipment source | Physical Equipment instance plus local Ability key |
| Item Bonus Action allowance | Activating Unit, Item Type and local Ability key |
| Item selection and consumption | Selected physical Item instance plus local Ability key |

`Unit.AbilityUseCounts` records spent uses by source/key. Remaining uses are computed from the declaration's maximum. An absent count means zero spent uses; creation and resume do not refill counters. Two supported Abilities using the same mechanic have independent keys and counters. `BonusActionUseCountsThisActivation` records activation allowances independently of persistent use limits and clears at activation end. World effects still control the allowed number of Bonus Actions and ordinary Actions.

Equipment and Item Abilities currently require unlimited usage. Equipment charges are not implemented. A Single Use Item's lifecycle is physical consumption, not a one-use Ability counter: the selected object moves from inventory to discard when its choice is applied. Multiple copies of the same Item Type and local key share the per-activation Bonus Action allowance, while consuming only the selected copy. Renewal restores eligible native limited uses and cannot recreate an Item.

Candidate IDs are deterministic source/key identities with target information where needed. Unit entries use `ability:<escaped-key>`; Equipment and Item candidate prefixes include their physical Instance ID. Card entries use source Type and key. Display names do not affect any of these identities. Usage is committed centrally when a legal candidate is applied; concrete handlers resolve the mechanic and its established consequences. Ordinary Action budgets remain shared across sources.

## Mechanics and supported contexts

Use `MechanicCatalog.All` for authoritative discovery; the following is an overview, not a second validation table.

| Mechanic | Established contexts |
| --- | --- |
| Heal, Holy Wave, Fireball, Stand Adjacent Friendly | Unit, Equipment or Item Action |
| Self Modifiers | Unit, Equipment or Item Bonus Action; Unit or Equipment Passive |
| Adjacent Modifiers | Unit or Equipment Passive |
| Extra Actions | Unit, Equipment or Item Bonus Action |
| Phase, Open Door | Unit or Equipment Passive / Free Action respectively |
| Cleave, Move After Attack, Advance On Kill | Concrete Unit After Attack paths |
| Explosion, Undying | Unit defeat consequence / immediate defeat replacement |
| Fury, Backstab, Transport Resource | Concrete Unit Passives |
| Telekinesis, Fire Breath, Claw Attack, Try Open Door, Summoning, Resource Actions, Shoot Shell | Concrete Unit Actions |
| Swap, Displace, Reshape Room, Place Banana Peel, Boost | Concrete Unit Bonus Actions |
| Run Rails, Waypoint Slot Machine | Instead of Activation / End Activation respectively |

The catalog exposes allowed usage and per-source multiplicity too. Specialized mechanics remain single instances per owner where existing execution depends on one concrete follow-up, capability or ordering. This does not imply arbitrary stacking, Equipment versions of every Unit mechanic, Item Passives, or freely interchangeable trigger timings. Specialized handlers remain concrete Engine code, using typed local payloads. Waypoint Slot Machine's six-face Resource output table is authored in its payload.

Passive modifiers retain ordinary posture and adjacency semantics. Holy Wave remains legal without an Upright hostile recipient and still lays down the acting Unit. Fireball planning reads available sources and actual usage restrictions; exhausting one source does not exhaust another. Mechanic validation is structural; dynamic legality still uses authoritative state, physical geometry, budgets, posture, targets and Fog of War.

## Validation, presentation and persistence

`AbilityContent.Validate` checks local keys, names, usage, multiplicity, descriptions and the Mechanic Catalog contract for the owner's source family. Scenario creation validates referenced Unit, Item, Equipment and Resource Types as appropriate. Diagnostics identify owner, list index and field. Invalid declarations are not reinterpreted by fallback adapters.

Descriptions, timing, use limits and source labels come from local declarations and registered mechanic metadata. `DescriptionOverride` may refine wording but never changes execution; parameterized descriptions must retain the required placeholders. Unit cards and inventory projections use the current game's definitions, including renamed scenario-local content. Fog of War filters those projections and resolution observations through the existing observation boundary.

Scenario JSON, DELVE1 transport, Designer import/export and Playtest carry `localAbilities`. New saves include the current owner definitions, generic counters, physical sources and concrete continuation context. Pending choices are rebuilt from authoritative state on resume. Replays are deterministic for the current model and seed.

Old global Ability definitions/grants, native Ability fields, compatibility bindings, dedicated Heal/Holy Wave/Fireball/Cleave counters, and old candidate/event formats have been removed. Their historical save formats are unsupported. Removed authoring fields are rejected rather than silently normalized. Unrelated Loot, World Card and terrain compatibility projections remain where their current content APIs still use them; they do not provide an alternative Ability model.

## Playtest examples and boundaries

Open **Home → Test Scenarios → Local Abilities - Pocket Workshop → Play scenario** (`#scenario/local-abilities-owned`). Threadkeeper demonstrates a limited local Action and Passive, Amber Chime a local Holy Wave Equipment Action, Folded Remedy a physical healing Item, and Spare Moment the per-Type Bonus Action allowance across duplicate Item copies. **Local Abilities - Three Sources** (`#scenario/local-abilities-three-sources`) compares Holy Wave on a Unit, Equipment and Item.

These scenarios are mutable playtest content in `ProjectDelve.Web/Content/Library/library.json`. Tests use dedicated fixtures for mechanical assertions and only check authored scenarios for validity and successful instantiation.

Normal Attack availability, base stats, movement patterns, terrain restrictions, footprint, Resource Storage and automated Behaviors remain structural Unit properties. They are not alternate named Ability declarations. `UnitAction` identifies concrete candidate/Attack operations; only Normal Attack/None remains authored in `UnitType.Actions`. Attack ordering preserves existing automated tie-breaking and does not supply mechanic parameters.

Inheritance, runtime content mutation, arbitrary triggers, scripting, Equipment charges and full canonical JSON catalog export/load remain outside this model. Content Designer now edits Equipment-owned Abilities through these contracts. Adding content using an already supported mechanic/context needs only its owner-local declaration; adding a new mechanic still requires a concrete Engine implementation and catalog contract.
