# Vision and Machines

This guide describes the current typed implementation. [CONSTRAINTS.md](CONSTRAINTS.md)
remains the rules authority. [Ability content](ABILITY_CONTENT.md),
[Equipment content](EQUIPMENT_CONTENT.md) and the [Mechanic Catalog](MECHANIC_CATALOG.md)
describe the shared authoring boundary.

## Ordinary vision and Reveal

`UnitType.SightDistance` defaults to 5 and must be nonnegative. The effective
value adds supported self, adjacent-Unit, Equipment and current-activation
`Stat.SightDistance` modifiers, then clamps to zero. Ordinary Passive modifiers
require an Upright source; a Lying Unit still contributes its own vision.
SightDistance is independent of attack RNG.

`GameState.EffectiveSightDistanceOf` supplies the distance for ordinary FoW and
exploration planning. Detached Side observations retain authoritative effective
values so removing hidden modifier providers cannot change displayed results.
Every occupied footprint Cell contributes vision. Manhattan distance, straight
Cell-center geometry, corner passages and blocking-destination discovery use
the shared `FogOfWar.VisionArea` path. Units do not block vision; Trees block
attack LOS but not ordinary vision. Solid Rock, Machines, Walls and Closed Doors
block ordinary vision. Phase vision ignores these obstacles while retaining
its distance; attack LOS is unchanged.

The `RevealDistance` Ability payload has an explicit nonnegative `Distance`
(`revealDistance` / `distance` in JSON). Unit, Equipment and Item declarations
support Action, Bonus Action and Free Action timings through normal source-aware
commitment. Unit and Equipment sources may be limited or unlimited; Items use
their ordinary consumption lifecycle. Reveal permanently adds Cells to its
acting Side's exploration, from every footprint Cell with effective Phase. It
does not modify SightDistance. A standalone Reveal remains legal with no new
discovery, including when FoW is disabled, and accepted invocations still pay
their ordinary costs and usage.

## Persistent occupancy capabilities

Native and Equipment `Phase` and `Amphibious` declarations are unlimited
Passives that remain effective while Lying. `UnitType.Amphibious` also supplies
Water permission. Shared `HasPhase` and `HasAmphibious` queries apply to initial
inventory, placement, movement, hypothetical positions, summons, restore and
external relocation. Ordinary Posture restrictions on performing Moves and
Actions still apply.

Amphibious permits Water traversal and occupancy while retaining terrain
restrictions, Edges, figures, Machines, footprint and FoW checks. Phase permits
its established traversal but still requires a normally legal endpoint. Combined
Phase and Amphibious can therefore end on otherwise legal Water, including
remaining there while Lying. Equipment Station redistribution checks the
resulting occupancy before transferring a movement-capability grant.

## Machine declarations and operation

`ScenarioDefinition.Machines` contains ordinary physical Machine instances.
Each has an Instance ID, position, `MachineType`, optional `OwnerSide`, stored
Resources and `UseCounts` keyed by Side. A null owner means public access;
ownership is independent of Machine identity or display name. Machines occupy
one Cell and block movement, attack LOS and ordinary FoW vision.

`MachineType.Storage` is optional. `MachineOperation` declares Resource costs
and one typed effect. It can produce a fixed Resource Type or use a six-face
output table, reveal with `MachineReveal(Distance)`, or redistribute objects
with `EquipmentStationOperation`. Only Resource output requires a Resource-free
operator Cell. Authored costs come from the Machine's storage.
`RequiredTerrainTypeId`, when present, is checked at creation.

`MaxUsesTotal` and `MaxUsesPerSide` are optional nonnegative operation limits;
zero makes the corresponding operation unavailable. Every successful Operate
increments the operator Side's Machine count. Total uses sum those counts.
Creation validates the Side keys, nonnegative counts and declared limits.
These counters belong to the physical Machine and survive save/continue and
ordinary scenario serialization, independently of the actor's Ability usage.

Operate uses the actor's source-bound Ability and ordinary Action eligibility,
public/own-Side access, adjacency with geometric LOS, no intervening impassable
Edge, paid Machine inputs and remaining Machine uses. Resource output requires
an empty Resource destination. Machine Reveal requires enabled FoW and at least
one newly discoverable Cell. It reveals for the operator's Side from the Machine
Cell through shared vision geometry, excluding only that source Machine as a
blocker. The operator's Phase does not propagate to Machine vision.

Canonical `MachineTypes.Beacon` is public on Stone Floor, with no storage or
Resource costs/output, Reveal Distance 15, no total limit and one use per Side.
Its lantern animation never explores Cells automatically. Civilization places
Beacons as standalone Machines with bounded deterministic attempts and
protected open pads; previews expose placement and omission counts. See
[World Generator Designer](WORLD_GENERATOR_DESIGNER.md).

## Equipment Station interaction

`PendingMachineInteraction` captures the Machine, operator, operating Side,
and source-bound Operate candidate. Its typed Station payload records the
eligible Units and opening allocation. The Engine can serialize this boundary without
spending the Action or Ability, paying Costs, or advancing the RNG. Cancel
returns to the activation. Confirm validates and commits the full allocation
and ordinary Operate accounting before publishing the result.

The Station owns the pool of eligible recipients, proposed assignments,
diagnostics, and normalized preview. Each Unit is evaluated alone, Upright and
at maximum HP through existing Engine queries. The preview retains physical
object IDs and use information. Confirmation validates the resulting inventory
on the live board, including movement-capability occupancy.

A Single Use Item that supplies Operate stays with its owner while the decision
is open. The transfer pool and projected resulting inventory exclude that Item.
Confirm consumes it through the ordinary Ability path and requires a separate
actual transfer. Cancel preserves the Item.

`POST /api/game/machine/preview` takes `sessionId`, `expectedRevision`,
`decisionId`, and an optional `allocation`. A request without an allocation
previews the saved draft if present, or the opening allocation. The response
contains the preview and saved draft. Previews do not save or advance play.
Submit `machine/confirm` with `equipmentAllocation` or `machine/cancel`
through the existing game command endpoint. Invalid proposals publish a
recoverable error with `equipmentDiagnostics` for the same pending decision.
To save provisional edits, include `equipmentAllocation` on `expedition/save`.
An invalid allocation can be saved as a draft and will be re-evaluated on resume.

The observed human decision exposes the Station roster, opening allocation and
any reserved Item. Choosing Operate opens a modal editor. On desktop, Unit
cards compare loadouts side by side. At narrow widths, the Unit selector shows
one loadout at a time. Use **Give to** on an individual object to change its
proposed owner. Copies have separate labels. A preview returns normalized
stats, movement and source-specific Ability definitions with remaining uses.
Opening-stat details, Ability sources and object details expand on demand.
Reset restores the opening pool. Cancel returns to the activation. Confirm
becomes available when the Engine accepts the complete allocation. In an
Expedition, **Save draft** checkpoints the current proposal. **Back to
Expeditions** then returns to the saved list. Resume reopens the editor and
requests a fresh Engine preview of the saved allocation.

### Play the workshop

Open **Scenarios**, find **Equipment Station Workshop** and select **Play
scenario**. It is also in the **Test Scenarios** campaign. Choose **Start
round**, **Stay**, **Operate**, then the Equipment Station target. The
Barbarian is the only awake Unit. All four friendly Units participate.

- Give the Barbarian's Lucky Boots to the Cleric. Their isolated MOV values
  change from 4 to 3 and 3 to 4 respectively. The Cleric stays wounded and
  Lying on the board.
- Give the Berserker Ring to the Cleric. Berserk follows the ring with one of
  its two uses remaining. The two Second Wind Items can go to different Units.
- Move the Rogue's Swim Tube to another Unit. The Rogue still occupies Water.
  The Engine flags the proposed placement and keeps the draft editable. Return
  the tube to the Rogue to confirm the other transfers. The Grunt accepts Items
  and cannot own Equipment.

The authored record is `equipment-station-workshop` in the [bundled
library](ProjectDelve.Web/Content/Library/library.json). It carries the
published Berserker Ring definition for portable imports and a scenario-local
Item ownership permission for the Grunt. Paste the [DELVE1
export](scenarios/equipment-station-workshop.delve1) into **Settings → Game →
DELVE1 scenario → Load scenario** to open an independent copy. The library
transport endpoint is `/api/library/scenarios/equipment-station-workshop/transport`.

The inspected desktop and mobile Edge captures and their index are in
[`.playwright-cli/equipment-station/REPORT.md`](.playwright-cli/equipment-station/REPORT.md).

Default automation skips interactive Operate candidates. An existing
candidate-key Decision Provider can cancel an open interaction. The typed
Engine confirmation and Web player command carry the full allocation.

## Visibility and automation

FoW observations and browser projections include Machines only when their
Cells are explored. Neutral ownership does not expose an unknown Machine.
Known Machines expose their operation and use state through normal projections;
clients submit authoritative choices rather than reconstructing legality.

`PreferBeacon` is an opt-in behavior preference over supplied legal choices.
It considers known public/own-Side Reveal Machines with remaining uses under a
Normal Order, an Upright actor, a usable Action-timed Operate source, an Action
remaining and enabled FoW. It can Operate now, Stay before operation, or choose
a legal Move reaching an operating position this activation. It avoids known
Lava, Cell Marker and Loot Marker arrivals for that preference and rechecks at
the next decision. Ranking is deterministic; it keeps no persistent goal and
does not inspect hidden geometry. Worker behavior continues to evaluate ordinary
legal Machine operations through its existing resource workflow.

`ExploreFoW` uses the actor's decision-time effective SightDistance and Phase
with known geometry when ranking full canonical movement paths. Actual swept
vision reevaluates effective modifiers at each real transit position. Planning
substitutes opaque placeholders for unknown terrain rather than consulting it;
Phase retains its established vision obstacle exception. A larger sight stat or
Reveal never permits movement through unexplored Cells or explicit selection
of unknown contents.

Focused behavioral coverage lives in `VisionCapabilityTests`, `BeaconTests`,
`BeaconBehaviorTests` and `BeaconGenerationTests`, using dedicated test-owned scenarios. Current
scenario transport and save/replay preserve typed declarations, physical
Machine identity, Side exploration and use counts.
