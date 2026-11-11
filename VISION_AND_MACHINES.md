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
remaining there while Lying. Losing Equipment on restricted terrain has no
new removal or forced-relocation mechanic.

## Machine declarations and operation

`ScenarioDefinition.Machines` contains ordinary physical Machine instances.
Each has an Instance ID, position, `MachineType`, optional `OwnerSide`, stored
Resources and `UseCounts` keyed by Side. A null owner means public access;
ownership is independent of Machine identity or display name. Machines occupy
one Cell and block movement, attack LOS and ordinary FoW vision.

`MachineType.Storage` is optional. `MachineOperation` declares Resource costs
and either one Resource output (fixed Type or six-face output table), or a
typed `MachineReveal(Distance)`. Reveal has no Resource output and does not
require a Resource-free operator Cell. Authored costs still come from that
Machine's storage. `RequiredTerrainTypeId`, when present, is checked against
the underlying terrain at creation.

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
