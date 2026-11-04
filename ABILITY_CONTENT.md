# Owner-local Abilities

Units, Equipment and Single Use Items own their complete Ability declarations. Each declaration contains a stable owner-local key, display name, timing, usage restriction and typed mechanic payload, with an optional description override. `LocalAbilityDefinition` is the C# and JSON model; there is no global Ability catalog, grant reference or legacy native Ability representation.

The [Mechanic Catalog](MECHANIC_CATALOG.md) registers reusable Engine operations and their supported contexts. It owns parameter constraints, validation and description metadata. It does not own authored Abilities or runtime state. Names and local keys never select an implementation.

[Content Designer](CONTENT_DESIGNER.md) uses those contracts to edit Unit-, Equipment- and Item-owned
Abilities through forms. Modifiers and capabilities remain ordinary local Abilities;
there is no separate editor gameplay model.

## Authoring

### Typed Costs and commitment

`LocalAbilityDefinition.Costs` is an ordered typed collection, separate from its
mechanic. `LayDownSelf` and `SpendResource(resourceTypeId, amount)` describe
payments for an accepted attempt. Empty Costs are omitted from JSON to preserve
compact definitions. Authored nonempty arrays
retain their order, including repeated Resource payments.

Active Abilities execute Costs through shared authoritative commitment. Eligibility
includes cumulative affordability before timing, usage, Item consumption or Costs
change. The authoritative candidate binds its source and targets; commitment
spends timing and usage, consumes the selected Item, then pays Costs in authored
order. No decisions or automatic consequences interleave those payments. The
mechanic resolves afterward against the resulting state. Failed rolls and
zero-effect attempts retain payment; rejected, stale, unaffordable or cancelled
selections pay nothing. Pending Attack and Door rolls resume their existing
concrete contexts without paying again, even if the source is Lying or its Item
was consumed. Initial invocation eligibility is never rerun after payment.
Content Designer edits ordered Costs through the shared Ability form using the Engine-owned Cost Catalog and recursive typed parameter fields. Unit, Equipment and Item sources use the same editor; unsupported contexts remain unavailable for Cost additions.

Attack and Door continuation context also captures a narrow `AbilitySource`
reference (source kind, Type ID, physical Instance ID and local key), independently
of the stable Action ID. Dice and operation events retain that attribution across
save/resume, including when duplicate Items share the same Type and local key.
Observations redact the reference when its bearing Unit is hidden. This reference
contains no copied Ability definition and is scoped to the resolving operation.

Resource payment uses only the bearer's Side + Unit Type shared store and retains
its actual Resource payload in events. Repeated Resource costs are cumulative.
Costs do not grant storage, borrow from Machines or other groups, or replace Machine recipe inputs. Post-payment effective stats apply when constructing an Attack; its selected targets remain bound.

Holy Wave declares `LayDownSelf` before `adjacentUnitsPostureChange(Enemies, Lying)`. The source lies down before recipients resolve; the committed operation still completes and empty recipient sets remain legal. Summon Troll, Place Banana Peel, Boost and Shoot Shell declare Diamond, Banana, Mushroom and Shell Costs respectively. Their mechanics contain no implicit payments. The same operation may be authored without a Cost or with another supported Cost.

All active declarations use the current typed mechanics, including scenario-owned Equipment and Items. The application-local Workspace remains mutable, with **Scenario > Workspace > Canonical** resolution. Replaced mechanic discriminators are unsupported; this migration does not add historical Library revisions, pins or save adapters.

Canonical content lives in [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs), [EquipmentRoster.cs](ProjectDelve.Engine/EquipmentRoster.cs) and [ItemRoster.cs](ProjectDelve.Engine/ItemRoster.cs). A declaration in any of these owners produces the same typed data as scenario-local JSON. Small C# helpers may construct or copy declarations, but no second definition is registered or consulted at runtime.

```csharp
var medic = new UnitType("field-medic", 3, 1, 1, 1, 1)
{
    LocalAbilities =
    [
        new("mend", "Heal", AbilityTiming.Action, new AdjacentUnitHealMechanic(RelationshipSelector.Friendlies, Amount: 2))
            { Usage = new(MaxUses: 2) },
        new("ward", "Aura", AbilityTiming.Passive,
            new AdjacentUnitsStatModifiersMechanic(RelationshipSelector.Friendlies, [new(Stat.Def, 1)]))
    ]
};
```

The equivalent concise roster entries are:

```csharp
Ability("Heal", Uses(2), AdjacentUnitHeal(RelationshipSelector.Friendlies, amount: 2), id: "heal"),
LocalAbility(new("holy-wave", "Holy Wave", AbilityTiming.Action,
    new AdjacentUnitsPostureChangeMechanic(RelationshipSelector.Enemies, Posture.Lying))
    { Usage = new(2), Costs = [new LayDownSelf()] }),
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
        "usage": { "maxUses": 2 }, "mechanic": { "kind": "adjacentUnitHeal", "selector": "Friendlies", "amount": 2 } },
      { "key": "ward", "name": "Aura", "timing": "Passive",
        "mechanic": { "kind": "adjacentUnitsStatModifiers", "selector": "Friendlies", "modifiers": [{ "stat": "Def", "amount": 1 }] } }
    ]
  }],
  "equipmentTypes": [{
    "id": "quiet-lantern", "name": "Quiet Lantern",
    "localAbilities": [{ "key": "use", "name": "Curtain Call", "timing": "Action",
      "costs": [{ "kind": "layDownSelf" }],
      "mechanic": { "kind": "adjacentUnitsPostureChange", "selector": "Enemies", "newPosture": "Lying" } }]
  }],
  "itemTypes": [{
    "id": "paper-lantern", "name": "Paper Lantern",
    "localAbilities": [{ "key": "use", "name": "Quiet Please", "timing": "Action",
      "costs": [{ "kind": "layDownSelf" }],
      "mechanic": { "kind": "adjacentUnitsPostureChange", "selector": "Enemies", "newPosture": "Lying" } }]
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

`Unit.AbilityUseCounts` records spent native uses by source/key. Remaining uses are computed from the declaration's maximum. An absent count means zero spent uses; creation and resume do not refill counters. Two supported Abilities using the same mechanic have independent keys and counters. `BonusActionUseCountsThisActivation` records activation allowances independently of persistent use limits and clears at activation end. World effects still control the allowed number of Bonus Actions and ordinary Actions.

Supported active Equipment Abilities may be limited or unlimited. Their spent counts live on the physical `OwnedObject.AbilityUseCounts`, keyed by local Ability key, and remain with that object through defeat, ground placement and later pickup. Ground and inventory projections show the same remaining uses. Unit counters remain on the individual Unit. A Single Use Item's lifecycle is physical consumption, not a one-use Ability counter: its envelope remains unlimited and the selected object moves from inventory to discard when its choice is applied. Multiple copies of the same Item Type and local key share the per-activation Bonus Action allowance, while consuming only the selected copy. Renewal restores limited Unit and currently owned Equipment uses, capped at their maxima; ground Equipment is unaffected and consumed Items are not recreated.

Candidate IDs are deterministic source/key identities with target information where needed. Unit entries use `ability:<escaped-key>`; Equipment and Item candidate prefixes include their physical Instance ID. Card entries use source Type and key. Display names do not affect any of these identities. Usage is committed centrally when a legal candidate is applied; concrete handlers resolve the mechanic and its established consequences. Ordinary Action budgets remain shared across sources.

## Mechanics and supported contexts

Use `MechanicCatalog.All` for authoritative discovery; the following is an overview, not a second validation table.

| Mechanic | Established contexts |
| --- | --- |
| Adjacent Unit Heal, Adjacent Units Posture Change, Adjacent Unit Posture Change | Unit, Equipment or Item Action |
| Fireball | Unit, Equipment or Item Action, Bonus Action or Free Action |
| Self Stat Modifiers | Unit, Equipment or Item Bonus Action; Unit or Equipment Passive |
| Adjacent Units Stat Modifiers | Unit or Equipment Passive |
| Extra Actions | Unit, Equipment or Item Bonus Action |
| Phase | Unit or Equipment Passive |
| Open Door | Unit, Equipment or Item Action or Free Action |
| Cleave, Move After Attack, Advance On Kill | Concrete Unit After Attack paths |
| Explosion, Undying | Unit defeat consequence / immediate defeat replacement |
| Fury, Backstab, Transport Resource | Unit or Equipment Passive |
| Telekinesis, Try Open Door | Unit, Equipment or Item Action |
| Swap, Displace, Reshape Room | Unit, Equipment or Item Bonus Action |
| In-Range Enemy Units Attack, Claw Attack, Adjacent Unit Summon, Resource Actions, Target Unit Attack | Unit, Equipment or Item Action |
| Adjacent Banana Peel Placement, Self Extra Moves | Unit, Equipment or Item Bonus Action |
| Run Rails, Waypoint Slot Machine | Instead of Activation / End Activation respectively |

The catalog exposes allowed usage and per-source multiplicity too. Active mechanics accept independent local declarations and source counters. Fury and Backstab bonuses add when each declaration's conditions hold; Transport Resource is an idempotent capability and retains its one-Cell, own-Move restrictions. Specialized mechanics remain single instances per owner where existing execution depends on one concrete follow-up, capability or ordering. Item Passives, finite-use Passives and freely interchangeable trigger timings remain unsupported. Specialized handlers remain concrete Engine code, using typed local payloads. Waypoint Slot Machine's six-face Resource output table is authored in its payload.

Adjacent Unit Summon uses the selected declaration's Unit Type and initial Posture across all three sources. References resolve through the effective scenario Unit catalog, including scoped canonical overrides, rather than a content name or the bearer's native summon. Resource operations likewise use the selected declaration while retaining the bearer's shared Side + Unit Type storage and ordinary Machine ownership/capacity rules. Source reuse grants neither storage nor additional activation opportunities.

Passive modifiers retain ordinary posture and adjacency semantics. Holy Wave remains legal without an Upright hostile recipient and pays its source posture Cost first. Fireball planning reads available sources and actual usage restrictions; exhausting one source does not exhaust another. Mechanic validation is structural; dynamic legality still uses authoritative state, physical geometry, budgets, posture, targets and Fog of War.

## Validation, presentation and persistence

`AbilityContent.Validate` checks local keys, names, usage, multiplicity, descriptions and the Mechanic Catalog contract for the owner's source family. Explicit content composition validates referenced Unit, Item, Equipment and Resource Types. Games capture those definitions, including transitive summons, before play; supplied runtime catalogs never fall back to current canonical rosters. See [content composition](CONTENT_COMPOSITION.md). Diagnostics identify owner, list index and field. Invalid declarations are not reinterpreted by fallback adapters.

Descriptions, timing, use limits and source labels come from local declarations and registered mechanic metadata. `DescriptionOverride` may refine wording but never changes execution; parameterized descriptions must retain the required placeholders. Unit cards and inventory projections use the current game's definitions, including renamed scenario-local content. Fog of War filters those projections and resolution observations through the existing observation boundary.

Scenario JSON, DELVE1 transport, Designer import/export and Playtest carry `localAbilities`. New saves include the current owner definitions, generic counters, physical sources and concrete continuation context. Pending choices are rebuilt from authoritative state on resume. Replays are deterministic for the current model and seed.

Old global Ability definitions/grants, native Ability fields, compatibility bindings, dedicated Heal/Holy Wave/Fireball/Cleave counters, and old candidate/event formats have been removed. Their historical save formats are unsupported. Removed authoring fields are rejected rather than silently normalized. Unrelated Loot, World Card and terrain compatibility projections remain where their current content APIs still use them; they do not provide an alternative Ability model.

## Playtest examples and boundaries

Open **Home → Test Scenarios → Local Abilities - Pocket Workshop → Play scenario** (`#scenario/local-abilities-owned`). Threadkeeper demonstrates a limited local Action and Passive, Amber Chime a local Holy Wave Equipment Action, Folded Remedy a physical healing Item, and Spare Moment the per-Type Bonus Action allowance across duplicate Item copies. **Local Abilities - Three Sources** (`#scenario/local-abilities-three-sources`) compares Holy Wave on a Unit, Equipment and Item.

These scenarios are mutable playtest content in `ProjectDelve.Web/Content/Library/library.json`. Tests use dedicated fixtures for mechanical assertions and only check authored scenarios for validity and successful instantiation.

Normal Attack availability, base stats, movement patterns, terrain restrictions, footprint, Resource Storage and automated Behaviors remain structural Unit properties. They are not alternate named Ability declarations. `UnitAction` identifies concrete candidate/Attack operations; only Normal Attack/None remains authored in `UnitType.Actions`. Attack ordering preserves existing automated tie-breaking and does not supply mechanic parameters.

Inheritance, runtime content mutation, arbitrary triggers, scripting and full canonical JSON catalog export/load remain outside this model. Content Designer edits Unit-, Equipment- and Item-owned Abilities through these contracts, including supported usage and timing choices. Item consumption uses no persistent charge counter. Adding content using an already supported mechanic/context needs only its owner-local declaration; adding a new mechanic still requires a concrete Engine implementation and catalog contract.
