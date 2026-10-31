# Engine Mechanic Catalog

`MechanicCatalog` is the discovery and structural-validation boundary for the mechanics covered below. Its immutable descriptors contain data only: stable IDs, parameter schemas, supported source/timing/usage profiles, context requirements, targeting notes, dependencies and description metadata. Registration binds each descriptor to an existing typed payload and a concrete Engine validator. Execution stays in the existing family-specific Engine paths.

The Mechanic Catalog does not own Ability declarations or runtime identities. Milestone 2 adds `LocalAbilityDefinition` on Unit, Equipment and Item owners, using these same mechanic contracts; see [Ability authoring](ABILITY_CONTENT.md). Existing `AbilityDefinition`/`AbilityGrant` content remains supported through compatibility resolution. Neither a local key, a global reference nor a display name selects a mechanic implementation.

## Discovery and validation

```csharp
var heal = MechanicCatalog.Find("heal");
var modifiers = MechanicCatalog.Find("selfModifiers");
var descriptors = MechanicCatalog.All;

var diagnostics = MechanicCatalog.Validate(
    new HealMechanic(2),
    new(MechanicSource.Equipment, MechanicTiming.Action),
    path: "abilityDefinitions[0].mechanic");

var json = JsonSerializer.Serialize(descriptors,
    new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
```

`FindByPayload` resolves the descriptor for an existing typed payload. `Validate` returns structured diagnostics with field paths, codes and messages. `ThrowIfInvalid` uses the same validation and preserves the existing `ArgumentException` exception family. With no context argument, validation checks the payload alone; provide a context to validate a particular source, timing, use restriction and availability of a Unit. `MaxUses: null` means unlimited for Abilities, and no Ability-use accounting for the non-Ability lifecycles.

Metadata enums serialize as readable strings without changing scenario or saved-game JSON settings. No public descriptor contains a delegate, CLR type, executable expression or handler name. The API is in Engine; this milestone adds no HTTP endpoint or client-side rules implementation.

The schema describes the existing typed JSON boundary. `Required` concerns omission; `AllowNull` concerns an explicit null/default collection. For example, World modifiers may be omitted and default to an empty list, but a null list is invalid. Defaults come from the existing model where one exists. A positive Heal amount has minimum 1 and no additional upper bound; Immediate Loot and World healing retain their existing maximum of 100. These intentionally different domains are not normalized into a new rule.

## Supported contexts

Each `Profiles` entry is one supported combination. Clients must not combine source, timing and usage values from unrelated entries. The discovery `MechanicTiming` enum describes existing lifecycle points; it does not add timings to `AbilityTiming`.

| Shared mechanic IDs | Timing | Unit usage | Equipment usage | Item usage |
| --- | --- | --- | --- | --- |
| `heal`, `holyWave`, `fireball`, `standAdjacentFriendly` | Action | Limited or unlimited | Unlimited | Unlimited |
| `selfModifiers`, `extraActions` | BonusAction | Limited or unlimited | Unlimited | Unlimited |
| `selfModifiers`, `adjacentModifiers` | Passive | Unlimited | Unlimited | Unsupported |

All shared mechanics require an acting or bearing Unit, including Equipment and Item sources. No shared Free Actions, arbitrary triggers or Item Passives are advertised. An Item's consumption remains its physical-object lifecycle, not a one-use Ability restriction. `selfModifiers` and `adjacentModifiers` accept nonempty lists of ATK, MOV, RNG and DEF modifiers from -100 through 100; zero amounts and repeated stats remain valid.

`Context`, `Targeting` and `Dependencies` explain the Engine systems and recipients involved. They are descriptive metadata, not executable target predicates. Structural validity does not imply current gameplay legality: the authoritative queries still check Posture, Action budgets, remaining uses, ownership, adjacency, LOS, target health and Fog of War. An empty Holy Wave remains structurally valid and legal under its existing rules. No new Activation Bag requirement is inferred merely because a mechanic uses an activation.

## Coverage and family boundaries

| Family | Registered IDs | Existing execution/storage boundary |
| --- | --- | --- |
| Shared Ability | `heal`, `holyWave`, `fireball`, `selfModifiers`, `adjacentModifiers`, `extraActions`, `standAdjacentFriendly` | Existing `AbilityMechanic` payloads and grants |
| Native Unit | `cleave`, `telekinesis`, `fury`, `backstab` | Existing Unit fields/action slot; not grantable |
| Immediate Loot | `healCollector` | Collector/reveal lifecycle |
| Immediate World | `layDownAllUnits`, `setAllDoors`, `fullyHealUnits`, `healDamagedUnits`, `damageUnitsByAdjacency`, `replenishLimitedAbilities` | World reveal and existing consequence ordering |
| Continuous World | `worldModifiers` | Existing World modifier list while its card is active |
| Environmental production | `directionalResourceProduction` | Terrain production at the established Round Start point |

IDs match existing serialized discriminators for polymorphic payloads. Native Unit entries and `worldModifiers` identify existing non-polymorphic fields; `SerializedBinding` makes that distinction inspectable. Native entries are marked `Grantable: false` and `MaxPerSource: 1`. Discovery does not make them usable through an Ability grant. Cleave retains its native use counter and After Attack continuation; Telekinesis retains its native Action flag. Fury and Backstab ATK bonuses retain their existing unrestricted Int32 domain.

Mechanic contracts own parameter constraints and supported context profiles. Ability validation and the Loot, World and environmental validators call those same contracts. List member constraints are read from the descriptor by typed validators. Description templates and required placeholders for shared Abilities also come from their contracts. Other families retain their concrete Engine description formatters, including conditional wording and pluralization; their catalog templates describe the operation for inspection rather than replacing those formatters. Registration does not duplicate numeric constraints in a second client schema.

Outer content validation remains necessary. It checks IDs and references, duplicate grants, compatibility slots, counter integrity, exactly one Loot reward, World lifecycle/exclusive payload rules, scenario consistency and other family-level constraints. Description-override policies also come from the contracts: World cards retain their stricter blank/unknown-placeholder rejection, and a Continuous card requires `{modifiers}` only when its modifier list is nonempty. A valid mechanic payload alone is not a valid complete scenario. JSON import and Designer Playtest continue through normal Engine scenario creation, so these checks remain authoritative. Complex family rules stay concrete; the catalog is not a universal schema interpreter.

## Deferred coverage

The catalog is not yet an inventory of every Engine rule. These existing operations retain their current authoring and validators and have no mechanic descriptors in this milestone:

| Area | Deferred operations |
| --- | --- |
| Combat | Normal Attack, Fire Breath, Claw Attack, Shoot Shell |
| Doors | Open Door, Try Open Door |
| Movement | Phase, Run Rails, movement patterns, terrain restrictions |
| Follow-ups and defeat consequences | Move After Attack, Advance on Kill, Undying, Explosion |
| Summoning | Summon Adjacent, Summon Worker, Summon Troll |
| Resources and machines | Transport Resource, Resource Storage, Pickup Resource, Give Resource, Operate, Waypoint Slot Machine |
| Specialized Bonus Actions | Swap, Displace, Reshape Room, Boost, Place Banana Peel |

Fixed Component and Terrain assembly structures also remain in their existing content catalogs. These operations must not be presented as supported shared grants merely because the underlying implementation exists. Behaviors, footprints, uniqueness and inventory/loot policies remain outside this mechanic catalog slice.

Legacy native Heal/Holy Wave/Fireball fields, inline Equipment abilities and other compatibility projections remain supported by their existing adapters. Shared mechanic discovery describes the normalized payload, not every historical storage shape. In particular, older native Aura and simple Bonus Action fields retain their wider modifier-amount acceptance; loading them does not newly impose the shared payload's -100 through 100 range. No compatibility accounting keys, canonical content IDs, candidate identities or event identities move into the mechanic contracts.

Canonical JSON export, the full roster/DSL migration, runtime content mutation, a Content Designer and specialized execution-model migrations remain deferred. Owned local Abilities are supported by Milestone 2 without expanding this catalog's context profiles.

## Registering another mechanic

1. Establish its rules through `CONSTRAINTS.md` before adding a new rule. For an existing operation, inspect its actual validators, execution, targeting and serialization first.
2. Define or reuse its concrete typed payload. A polymorphic payload's registered ID must match its JSON discriminator. Do not infer an ID or behavior from a display name.
3. Add an explicit `MechanicRegistration.Create<T>` entry in the appropriate `MechanicCatalog` family registration method. Define each parameter constraint once in a `MechanicParameter`; pass that same object to `MechanicChecks` in the typed validator. Keep cross-field checks concrete and return useful field diagnostics.
4. Declare only implemented source/timing/usage profiles, context, targeting, dependencies and description metadata. Use a native/family binding and `Grantable: false` when the operation has not migrated to shared Abilities.
5. Route the existing content validator through the contract. Preserve reference/lifecycle checks at their family boundary. Retain the concrete execution path and source-specific accounting; registration does not implement execution.
6. Add tests for discriminator/registration coverage, metadata serialization, valid and invalid boundary values, context combinations, diagnostic paths and agreement with the authoritative content validator. Add meaningful gameplay/JSON compatibility coverage when execution or authoring integration changes.

`MechanicCatalog.Build` explicitly combines the family registrations and rejects duplicate IDs or payload types. It does not scan assemblies, read rosters or construct game state. Parameter defaults may come from inert model records; registration must not call content catalogs that in turn require mechanic validation. Resource references resolve during validation, after registration. This keeps discovery independent of roster initialization and avoids hidden global registration side effects.
