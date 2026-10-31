# Engine Mechanic Catalog

`MechanicCatalog` is the discovery and structural-validation boundary for the registered mechanics. Its immutable descriptors contain data only: stable mechanic IDs, parameter schemas, supported source/timing/usage profiles, Unit context requirements, targeting notes, dependencies, and description metadata. Explicit registrations bind those descriptors to typed payload validators. Execution remains in concrete Engine handlers.

Every Ability is an owner-local `LocalAbilityDefinition` in a Unit, Equipment, or Item definition. Its key, name, timing, usage, and `AbilityMechanic` payload are the authoritative declaration. There is no global Ability catalog, grant-reference model, or native-field adapter. A mechanic ID identifies reusable semantics; an owner-local Ability key identifies that owner's authored Ability. Neither a key nor a display name selects an implementation. See [Ability authoring](ABILITY_CONTENT.md).

## Discovery and validation

```csharp
var heal = MechanicCatalog.Find("heal");
var descriptors = MechanicCatalog.All;

var diagnostics = MechanicCatalog.Validate(
    new HealMechanic(2),
    new(MechanicSource.Equipment, MechanicTiming.Action),
    path: "equipmentTypes[0].localAbilities[0].mechanic");

var json = JsonSerializer.Serialize(descriptors,
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
```

`FindByPayload` resolves the descriptor for a typed payload. `Validate` returns structured field paths, codes, and messages. `ThrowIfInvalid` uses the same validation and throws `ArgumentException`. Without a context, validation checks only the payload. Supply a context to validate its source, timing, use restriction, and required Unit context. `MaxUses: null` means unlimited for an Ability; non-Ability lifecycles do not use Ability counters.

`AbilityContent.Validate` adds the owner-local envelope checks: nonblank distinct keys and names, supported timing and usage, mechanic multiplicity, and supported description overrides. `AbilityContent.ValidateState` checks the actual owner catalogs and counter integrity. C# authoring, JSON scenario import, Designer Playtest, and refreshed runtime state use these Engine validation boundaries. A valid payload alone is not a valid owner definition or scenario.

Metadata enums serialize as readable strings without changing gameplay JSON settings. Descriptors expose no delegates, CLR types, executable expressions, or handler names. The catalog is an Engine API; clients do not implement its execution rules.

[Content Designer](CONTENT_DESIGNER.md) consumes these descriptors for Equipment
Abilities and Immediate Loot forms, including nested parameters, reference pickers
and supported context combinations. Its definition-only validation endpoint uses
the same Engine checks as scenario creation; it does not require a running game.

`Required` describes omission; `AllowNull` describes explicit null/default collections. Defaults are recorded only when the typed model supplies them. Heal and Extra Actions require a positive amount without an additional upper bound. Immediate Loot and World healing retain their existing upper bound of 100. Fury, Backstab, Claw Attack, and Explosion retain their previously accepted Int32 parameter domains; the catalog does not silently tighten them. Canonical Explosion still declares damage 1.

## Ability source and timing profiles

Each `Profiles` entry is one complete supported combination. Do not combine source, timing, and usage values from unrelated entries. The gameplay `AbilityTiming` values represent the concrete boundaries already used by current content. `MechanicTiming` also describes the separate Loot, World, and Terrain lifecycles.

| Mechanic IDs | Timing | Unit usage | Equipment usage | Item usage |
| --- | --- | --- | --- | --- |
| `heal`, `holyWave`, `standAdjacentFriendly` | Action | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |
| `fireball` | Action, BonusAction, FreeAction | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |
| `selfModifiers`, `extraActions` | BonusAction | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |
| `selfModifiers`, `adjacentModifiers` | Passive | Unlimited | Unlimited | Unsupported |
| `phase` | Passive | Unlimited | Unlimited | Unsupported |
| `openDoor` | Action, FreeAction | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |
| `telekinesis`, `tryOpenDoor` | Action | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |
| `reshapeRoom`, `swap`, `displace` | BonusAction | Limited or unlimited | Limited or unlimited | Unlimited; consumes Item |

Equipment stat changes use local Passive `selfModifiers`; Phase and Open Door use their local typed payloads. There are no separate Equipment modifier/capability execution paths. Modifier lists accept ATK, MOV, RNG, and DEF amounts from -100 through 100, including zero and repeated stats. An empty modifier list is invalid; an Equipment definition with no stat changes simply has no such Ability. This preserves the previous direct Equipment modifier domain.

The remaining specialized mechanics currently support Unit sources only. Their local envelope is the same; the source restriction records implemented gameplay scope rather than a second model.

| Mechanic IDs | Timing | Usage |
| --- | --- | --- |
| `fireBreath`, `clawAttack`, `summonAdjacent`, `summonWorker`, `summonTroll`, `pickupResource`, `giveResource`, `operate`, `shootShell` | Action | Limited or unlimited |
| `placeBananaPeel`, `boost` | BonusAction | Limited or unlimited |
| `fury`, `backstab`, `transportResource` | Passive | Unlimited |
| `cleave` | AfterAttack | Limited or unlimited |
| `moveAfterAttack`, `advanceOnKill` | AfterAttack | Unlimited |
| `explosion` | WhenDefeated | Unlimited |
| `undying` | InsteadOfDefeat | Unlimited |
| `runRails` | InsteadOfActivation | Unlimited |
| `waypointSlotMachine` | EndActivation | Unlimited |

Open Door, Telekinesis, Try Open Door, Reshape Room, Swap and Displace accept independent declarations under distinct local keys, as do the shared active mechanics. Remaining specialized payloads retain `MaxPerSource: 1`, including Phase and Cleave, where current execution depends on one concrete capability, follow-up or ordering. Specialized names and use limits belong to the envelope: for example, Cleave's payload contains its trigger damage and damage, while its local Ability contains its name and optional maximum uses.

`waypointSlotMachine` authors a six-face `ResourceOutputTable`; both reward resolution and card text read it. Summon Worker and Summon Troll retain their concrete, rules-established target/resource semantics. Summon Adjacent instead authors its target Unit Type and initial Posture explicitly.

All Ability sources require an acting or bearing Unit. Item consumption remains the selected physical object's lifecycle, not a one-use Ability restriction. No Item Passives, arbitrary triggers, or unsupported source/timing combinations are advertised.

## Structural validity and gameplay legality

`ContextRequirements` describes required execution context. `UnsupportedContextReason` lists implemented combinations and their requirements; validation uses the same Engine explanation displayed by the Designer. These descriptions explain support without client-side mechanic eligibility rules.

`Context`, `Targeting`, and `Dependencies` explain the participating Engine systems and recipients. They are inspection metadata, not executable targeting predicates. Authoritative gameplay queries additionally check Posture, Action budgets, uses, ownership, adjacency, LOS, target health, placement, resource availability, and Fog of War. An empty Holy Wave retains its established legality. Structural validation does not infer a new Activation Bag membership requirement.

Specialized resolution stays concrete. Cleave uses the existing immediate post-Attack continuation and generic owner-local use counters. Undying is an immediate defeat replacement; Explosion uses the existing short-lived defeat snapshot and FIFO consequence queue. Run Rails replaces the ordinary activation. Waypoint rewards happen at activation end, after this Unit's own qualifying Move. Registering these timings does not create a generic trigger system or define new ordering rules.

## Other content families and remaining boundaries

| Family | Registered IDs | Resolution boundary |
| --- | --- | --- |
| Immediate Loot | `healCollector` | Collector/reveal lifecycle |
| Immediate World | `layDownAllUnits`, `setAllDoors`, `fullyHealUnits`, `healDamagedUnits`, `damageUnitsByAdjacency`, `replenishLimitedAbilities` | World reveal and existing consequence ordering |
| Continuous World | `worldModifiers` | Modifier list while its World Card is active |
| Environmental production | `directionalResourceProduction` | Established Round Start production |

Polymorphic registrations use the actual JSON discriminator as their stable ID. `worldModifiers` describes the existing non-polymorphic modifier list. `SerializedBinding` distinguishes that assembly boundary. Ability descriptors all describe `AbilityMechanic` payloads, including formerly specialized native fields.

Outer validators still check content references, finite-deck composition, exactly one Loot reward, World lifecycle/exclusive-payload rules, environmental assembly, valid Unit placement, and scenario consistency. Description-override policies come from the contracts: shared Abilities permit their established parameterized overrides; specialized local mechanics currently use their contract text; World Cards retain stricter blank/unknown-placeholder rejection and conditional `{modifiers}` handling. Concrete family formatters remain responsible for conditional wording and pluralization. Parameter constraints are not duplicated in a client schema.

The catalog is not an inventory of every Engine rule. Normal Attack availability and base stats, Footprint, uniqueness, movement patterns, terrain restrictions, Resource Storage, inventory/loot policies, automated Behaviors, and Fixed Component assembly remain typed structural content. Machine inputs/outputs and terrain placement retain their established content models. These structures do not become Ability payloads merely because they influence gameplay. The specialized Unit-only profiles above do not promise Equipment or Item support.

Canonical JSON export and a dedicated runtime-editing interface remain separate work. Content Designer now uses the catalog to edit Equipment Abilities and Immediate Loot mechanics. Current canonical C# and scenario JSON use the same local Ability model and validation; no format migration or duplicate global catalog is required.

## Registering another mechanic

1. Establish new rules through `CONSTRAINTS.md` before introducing them. For an existing operation, inspect its actual validation, execution, targeting, and serialization first.
2. Define its concrete typed `AbilityMechanic` or family payload. Its registered ID must match its JSON discriminator where applicable. Do not derive identity or behavior from a display name.
3. Add an explicit `MechanicRegistration.Create<T>` entry to the appropriate partial registration method. Define each parameter constraint once in a `MechanicParameter`, then pass that same metadata to `MechanicChecks` in its typed validator. Keep cross-field checks concrete and return useful field diagnostics.
4. Declare only implemented source/timing/usage profiles, multiplicity, context, targeting, dependencies, and description metadata. No profile implies execution support by itself.
5. Use the existing family validation boundary and concrete handler. Ability handlers consume the resolved local declaration and shared source accounting. Preserve family reference/lifecycle checks and concrete continuation semantics.
6. Add focused coverage for discriminator/registration agreement, metadata serialization, valid and invalid boundaries, supported contexts, field paths, and validation agreement. Add meaningful gameplay and JSON round-trip tests when integrating execution or authoring.

`MechanicCatalog.Build` explicitly combines registration lists and rejects duplicate IDs or payload types. It never scans assemblies, registers authored content globally, reads rosters, or creates game state. Registration may inspect inert model defaults; it must not call a content catalog that validates through the mechanic catalog. Resource references resolve during validation after registration. This keeps catalog discovery independent of roster initialization.
