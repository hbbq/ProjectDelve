# Storage and Enforcement V1

## Physical objects and ownership

A Storage is a labeled area off the board. Its serializable record contains `StorageId`, `Name`, nullable `SideId`, and `Contents`. Contents use the same `OwnedObject` record as Unit inventories, ground objects and the Loot deck. Each card retains its Instance ID, resolved Equipment or Item Type, Loot Card provenance, Level and per-Ability spent-Use counts. Unit inventories alone supply bearer Stats and Abilities. Further carried-object types can use the shared representation when their own rules are established.

Ordinary scenarios can supply `storages` in their setup JSON. Setup supplies the global `authority-repossessed-equipment` entry when omitted and recognizes its existing declaration on handoff. It requires the name **Repossessed Equipment Storage** and a null Side. Other Storages may reference a declared Side, independently of who carried an object. Restore checks Storage identity, object identity across every location, supported contents, Levels, provenance and counters. New Loot cards and purchases reserve occupied physical IDs.

`StorageRules.Deposit` and `Withdraw` compose a transfer on the Engine's resolution copy. They resolve the current source and destination, validate the resulting inventory and occupancy, then replace both locations. They use the existing inventory transformation to clamp HP. Storage grants no player command. A caller supplies the rule for access, payment or collection. The authority Storage has no withdrawal service in V1.

## Paul and the Action

Paul's canonical Unit Type is `paul-type`, displayed as **Paul, Enforcement Officer**. His Stats are MOV 3, RNG 1, ATK 0, DEF 1 and HP 1. His declaration supplies Open Door, prohibits the ordinary Attack, and grants Repossession as an Action. Designers can place the canonical Type or reuse its mechanic in another authored Ability. Paul uses the procedural figure.

```json
{
  "kind": "tryRepossessRandomEquipment",
  "successCount": 2,
  "destinationStorageId": "authority-repossessed-equipment"
}
```

The success count ranges from zero through six. Game creation resolves the destination against that scenario's Storages. An Upright actor selects one observable hostile Unit within its effective RNG and ordinary Unit line of sight. Level and active Equipment modifiers apply when choosing the target. RNG 1 includes diagonal neighbors, and RNG 2 or greater uses Manhattan distance. Each Equipment card is considered separately. Removing that particular card must leave the target able to occupy its complete current footprint. A Unit carrying only Items or occupancy-essential Equipment supplies no target.

Invoking the Action spends its ordinary opportunity and any declared source Uses or Costs. The pending d6 decision retains the range used to choose the target, including when a posture cost suppresses an Equipment modifier. It can be saved and resumed. A roll at or below the count succeeds. Failure leaves both figures and their inventories in place, apart from any ordinary invocation cost. On success the Engine sorts eligible physical cards by ordinal Instance ID and uses the existing uniform Loot index sampler. Its d6 rejection sampling gives each card the same probability, including when the number of cards does not divide six. A single eligible card requires no selection roll.

The selected card enters the configured Storage. The actor drops all its remaining physical objects at its former anchor and leaves play. Outcome snapshots include the transfer, drops and departure together. This departure bypasses defeat replacement, defeat consequences, defeat objectives, Loot rolls and Credits drops. The ordinary completed-activation boundary still evaluates objectives.

## Automated play

Under Normal Orders, the equipped-enemy Behavior considers hostile Units observable to its Side and recalculates Equipment eligibility at each decision. It considers reachable positions with sufficient effective RNG and line of sight to each target and ranks targets by route distance, anchor row, column and Unit ID. It chooses among legal movement candidates using remaining distance, movement cost, destination row, column and candidate key. Friendly figures permit transit, occupied Cells cannot be destinations, and other Sides block transit. The planner can account for an openable Door on the route or final sightline. The actor must still open the actual Door before passing or targeting through it.

With no suitable target Paul stays. Once in range with line of sight, he attempts repossession. After failure, his Behavior can finish the activation normally and retries on a later activation. The transient failure marker survives a mid-activation save. Human-controlled Units retain ordinary Action legality. Explicit Standing Orders continue to govern automated Units.

## Expedition and inspection

The world population pool gives Paul threat tier 5 and relative weight 1. Standard entries use relative weight 2. Difficulty weighting keeps him uncommon early and allows him in the normal population where available. There is no guaranteed introduction region. Existing authored encounter roles and their Orders still apply.

Region handoff transfers Storages alongside the Party and Side banks. Stored cards retain spent Uses across regions. The handoff includes the definitions and Loot provenance those cards require, falling back to saved definitions when current content omits them. Incompatible definitions fail validation before a successor can be published. Off-board holdings reserve IDs before the new region allocates its Loot deck. A departed Party member remains in the roster and returns through the ordinary handoff. Its dropped possessions stay with the old region.

An unrestricted, FoW-disabled table exposes Storage records through the existing game snapshot at `result.state.storages`. Side observations omit the holdings. Journal feedback identifies the confiscated Equipment by name and Instance ID when the target inventory was observable. Hidden targets disclose neither field. Exact inventory can also be inspected in an authoritative Expedition checkpoint. V1 has no general Storage management UI.

## Verification

Dedicated Engine fixtures exercise location uniqueness, both ownership kinds, Items, leveled Equipment and Uses, rejected transfers, 0/6 and 6/6 outcomes, uniform selection, source accounting, HP clamping, occupancy-dependent grants, departure drops, Fog of War, replay and pursuit. The pursuit fixtures include blocked paths, occupied chokepoints, a target behind its sole Door, ties and later activation retries. Web fixtures exercise typed authoring, feedback, saved Storage and a departing Party member's return with confiscated and dropped property accounted for separately.

The browser playtest used an isolated 7 × 3 table. Paul started at (0, 1) and an equipped Barbarian at (4, 1). In the Journal playthrough Paul moved to (3, 1) and rolled 5. He spent his Action and remained adjacent. In round two he rolled 1. The Journal identified **Lucky Boots (boots-evidence-01)** and recorded Paul's departure. The unrestricted game snapshot contained that exact card in the authority Storage. The Barbarian's inventory and the ground were empty. Only the Barbarian's figure remained, and the defeated-Unit set was empty.

| Suite | Result |
| --- | --- |
| Engine and generation | 3,995 passed |
| Web, including Designer and Expedition | 934 passed with test collections run sequentially |
| Node client and browser dispatcher | 1,185 passed, optional Beacon performance benchmark skipped |
