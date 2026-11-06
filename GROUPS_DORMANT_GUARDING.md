# Groups, Dormant and geographic Orders

This milestone extends ordinary Engine state and scenario authoring. It does not
add Population/Mission generation, a trigger language, formation movement or an
alternative AI/controller architecture. The gameplay authority is CONSTRAINTS.md.

## Authoring and activation

An activation identity is `(UnitTypeId, SideId, GroupId)`. `GroupId` is an optional
individual Unit/placement value; null identifies the default group. Use a figure
marker and matching bag token for named groups. Sharing a label across different
Types does not combine their tokens. Relationships, Unique limits,
`SameTypeSideLimit` and Resource pools remain Type + Side scoped.

Agency and initial Orders accept GroupId. Null-group rows provide setup defaults;
exact named rows override them. Setup materializes independent runtime assignments,
including independent Route cursors. An explicit named Normal Order suppresses its
default. Changing one runtime group never changes another through inheritance.
Controllers remain required; Orders remain sparse with Normal as their default.

Summon always creates a null-group Unit. Existing exact default-group controller
ownership wins; a newly introduced identity inherits its creator's controller.
It follows its own group's current Order or Normal, never its creator's Order.

Round Start inserts one token per living non-Dormant identity. Draw captures the
eligible matching IDs in `ResolvingTokenUnitIds`. Surviving captured members are
selected in current topmost/leftmost order; created or awakened nonmembers cannot
join an ongoing token. There is no Round Start per-Unit eligibility snapshot.
Empty tokens remain valid. No creation, defeat or wake edits the current bag.

## Dormant

`UnitPlacement.Dormant` defaults false. `WakeDistance` is an optional placement
override, then an optional Unit Type default, then the common default 8. Runtime
Units capture the effective distance. Negative values are rejected; zero is valid
and cannot directly detect a non-overlapping hostile at zero distance; an Attack
or a friendly Alert chain can still wake it.

Proximity is minimum raw Manhattan distance between occupied Cells of living
hostile Units. Walls, terrain, Doors, LOS and FoW do not affect it. Check the fully
instantiated initial setup before first-round effects; check after Round Start
effects before population; check after a whole token completes, never between its
members. Every living Unit with a hostile in its own WakeDistance initiates Alert,
including already awake Units. Recipients are determined together and removed
topmost/leftmost, then ID.

A valid committed Attack initiates Alert from every target, awake or Dormant,
before DEF capture and dice, regardless of miss, block or damage. No proximity
check is needed. Invalid candidates do not initiate Alert. Ordinary damage without
an Attack adds no trigger.

Alert immediately reaches the whole connected friendly network. A link is minimum
raw footprint Manhattan distance **3** or less, ignoring terrain, Walls, Doors,
LOS, Phase, FoW and pathfinding. Existing friendliness means the same Side;
other Sides, including neutral ones, cannot bridge the network. Every living
in-play Unit relays, awake or Dormant, regardless of Type or Group. Only Dormant
markers are removed. There is no Alert stat, flag, Ability, Order change, token
insertion or additional activation. A temporary queue and occupied-Cell lookup
visit each reached Unit once; no paths or alert history enter save state.
Marker events contain no source/path and retain event-time FoW filtering.

Physical play follows friendly links of at most three Cells, marking visited
figures temporarily if needed for a large cycle. Only removed Dormant markers
persist. Long chains can wake an entire tightly packed encounter, even through
walls; this follows the approved raw-distance rule. There are no defeat triggers,
alarm actions, sound simulation or return to Dormant.

Dormant changes activation only. Figures, occupancy, targeting, passives, World
Effects, Objectives and vision remain ordinary. `UnitWoken` uses existing
event-time observation filtering. A visible Dormant peer cannot reveal an entirely
hidden captured group's activation. Observations omit hidden participant IDs.

### Default selection evidence

`WakeDistanceEvaluationTests` runs a scripted MOV 4 approach from (0,2) to an enemy
at (24,2), with legal Engine choices. The obstructed case walks around a Wall.
Each case is repeated with Hero-side FoW, for twelve deterministic comparisons.

| WakeDistance | First wake, open | First wake, Wall detour |
| --- | --- | --- |
| 5 | Round 5 | Round 6 |
| 8 | Round 4 | Round 5 |
| 12 | Round 3 | Round 4 |

FoW does not change those results. Eight supplies one more approach activation of
lead time than five while avoiding twelve's earlier distant activation. This is
a reproducible timing comparison, not evidence of combat balance or a claim that
every encounter should use eight. A scenario can choose a smaller/larger override.

## Guard Area and Patrol Route

GuardArea has an on-board rectangular `area: { x, y, w, h }`. Ordinary pursuit uses:

- Engagement: effective MOV + RNG, minimum raw footprint Manhattan distance.
- Leash: effective MOV + RNG + 2 from the area; every occupied Cell must fit within
  that distance. Both the target and chosen ordinary Move destination are bounded.
- A supported Attack position must be reachable under existing analysis rules.
  Actual choices always remain normal Engine-legal candidates.
- With no suitable threat, return until the entire footprint is inside, then Stay.
  An impossible or unreachable area results in Stay, not a replacement destination.

The focused comparisons reject MOV-only engagement because it excludes the
Skeleton Archer's useful seven-Cell response (MOV 3, RNG 4). MOV+RNG+2 gives that
Archer a leash of nine instead of six from 2MOV; Goblin's leash becomes seven
instead of eight. MOV 0 ranged Units retain a useful range-based budget. Equipment
and current modifiers participate through authoritative effective stats.

Ranged positioning uses existing Maximize Attack Distance. Flee favors safe legal
positions wholly inside the area and returns when outside. ATK 0 without another
supported Attack does not attempt attack approach. Worker ignores these Orders as
before. Optional reusable `IgnoreLeash` removes pursuit bounds while retaining
engagement and legality; no canonical Type receives it implicitly. Existing higher
priority abilities and special follow-ups retain their established interpretation.

PatrolRoute reuses `FollowRoute` payloads, Waypoints, Routes and all three modes
(Once, Loop, PingPong). Its current waypoint is the threat/leash reference. A Move
started with suitable threats does not advance its cursor, even on incidental
waypoint arrival. Quiet movement resumes the same cursor/direction. Once completes
to Normal. There is no formation, remembered enemy, alert flag or dwell timer.

Ordinary FollowRoute is unchanged. Patrol progress is group-wide: individual
members can advance it sequentially. Long waypoint spacing can deliberately limit
which nearby threats qualify through the current-waypoint leash; place waypoints
appropriately for the desired patrol coverage.

## Demonstration

Open **Scenarios → Groups, Dormant & Patrols**, or its Designer copy. The compiled
definition is `ProjectDelve.Web/GroupsAndDormantScenario.cs`, ID
`groups-and-dormant`; it also enters the existing bundled scenario workflow.

The Heroes start west. Goblins in `camp` guard the eastern camp; an awake sentry
ensures its token exists while two members are Dormant. The separate `road` Goblins
patrol a Loop. An Archer demonstrates ranged guarding, a Shaman Flee (WakeDistance
5), and Zombies sleep inside the southeastern house. Both Sides have No Objective.
Hero FoW is enabled; Designer is omniscient.

Move east and inspect the Journal. Depending on draw order, waking camp members
either join the still-undrawn camp token or wait for the next round. The entirely
Dormant house initially supplies no token. Waking through a Wall is intentional.
Shaman's ordinary summon can introduce null-group Goblins; it never joins `camp`.
Use group controller settings to compare automated and manual choices. Designer
exposes individual markers/distances, group Orders, rectangles and Route progress.

The demonstration is mutable authored content. Strict mechanical assertions use
small dedicated test-owned scenarios, not its current layout or roster.

## Verification and scaling

Focused tests cover identity/counts, draw-time membership, creation and null-group
Summon, copied controller/Order defaults, independent Route progress, empty tokens,
initial/token/Round Start waking, invalid/missed/blocked/lethal and granted Attacks,
multi-recipient waking, passives/World damage, replay, FoW and Designer/DELVE1.
Area fixtures include melee/ranged/Flee/Worker/ATK0/special abilities, equipment,
2x2 actors/targets, barriers, known space and route interruption/resumption.

Wake checks use temporary figure/footprint indexes and hostile lists by Side,
without pathfinding or a persistent spatial subsystem. Alert uses one temporary
same-Side occupied-Cell lookup, searching the 25-Cell Manhattan diamond around
each relay's occupied Cells. It visits each reached Unit once; it does not scan
the whole population again for every link. Proximity-source discovery still uses
bounded Unit comparisons, and ordinary wake events still copy/observe state.
If no living Dormant Units remain, the calculation is skipped because Alert has
no other consequence. A 100-enemy fixture retains
all 101 figures but reduces activation starts from 101 to 1 and token draws from
2 to 1 while those enemies are Dormant. This measures skipped operations, not a
hardware latency guarantee. Dormant still incurs ordinary observation, effects,
occupancy, state-copy and rendering costs.

Whole-area return uses one cached reverse distance field per actor/area/traversal
within a detached decision snapshot, rather than one full-board search per Move
candidate. A separate forward-search oracle checks Doors, Phase, FoW and footprints.
No mutable global path cache is introduced.

Validation commands use .NET 10 and the existing isolated artifacts directory:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test tools/test-browser-qa.cjs ProjectDelve.Web.Tests/*.test.cjs
```

Use the installed Node/Python selection described in AGENTS.md and
BROWSER_INSPECTION.md when shims or Pillow lookup require it. Edge desktop/mobile
QA uses the protected dispatcher, an isolated 5081 host and isolated authoring
stores, preserving the existing 5080 game. Captures/logs live under ignored
`.playwright-cli/`.

Existing authored files omit GroupId/Dormant safely. No obsolete runtime-save
migration or historical generator-output compatibility layer is introduced.

### Verified results

On .NET 10.0.12, the solution build succeeded. Final full runs: Engine 2,595
passed, zero failed; Web 690 passed and twelve known failures in
`ScenarioContentReferenceTests`, all reporting conflicting Equipment identity
`knightly-boots`. These pre-existing reference-fixture failures were recorded
separately; no content validation was weakened. The build also retains the cached
NU1900 warning because NuGet vulnerability metadata is unavailable.

All 981 Node/client/dispatcher tests passed with the installed Pillow-capable
Python selected. Desktop 1440x1000 and mobile 390x844 Edge checks covered Group and
Dormant editing, Guard/Patrol parameters, DELVE1 export/import, isolated Library
save/reopen, FoW gameplay and visible 3D Group/Dormant labels. A dedicated QA
fixture confirmed the marker disappears while its valid Attack is still awaiting
the first dice roll. Successful inspection reports contain no console errors or
failed requests. The QA host was stopped; the original 5080 listener was preserved.

### Universal Alert verification

`AlertPropagationTests` adds fifteen cases covering awake/Dormant initiators,
mixed chains (including a Lying relay), three-Cell gaps, hostile/neutral barriers,
cycles, terrain/Doors, multi-Cell footprints, valid/invalid/missed/blocked/lethal
Attacks, simultaneous targets on different Sides, pre-dice resolution, undrawn
versus captured participants, removed/defeated bridges, FoW and JSON continuation.
The attacked-Goblin fixture reaches multiple sleeping Goblins beyond the initial
three-Cell radius through awake and newly awakened friends. Web fixtures cover
DELVE1 and the complete projected response, ensuring hidden relay/recipient IDs
and wake events stay absent.

After Alert: solution build succeeded; Engine **2,610 passed**, Web **692 passed**
with the same twelve pre-existing `knightly-boots` reference-fixture failures;
all **981 JavaScript/client/security tests passed**. The existing cached NU1900
audit metadata warning remains. Python/Pillow was selected using the repository
environment override for asset verification.

An isolated 144-enemy measurement retained all 145 figures, reduced activation
starts **145 to 1**, and reduced submitted decisions **143 to 0** for one Round.
Measured setup-plus-Round elapsed time was **726 ms awake versus 136 ms Dormant**
in that run. These are illustrative local measurements, not latency guarantees;
concurrent full-suite runs varied substantially. No timing threshold is asserted.
Alert has not removed the activation-work savings for distant sleeping crowds.

Desktop 1440x1000 and mobile 390x844 Edge checks use a test-owned imported fixture:
an attacked awake Goblin, an awake relay, and two Dormant Goblins at successive
three-Cell links. Both markers disappear before the first Attack dice roll,
without changing the existing token count. The same workflow is checked behind
a Wall with Blue FoW enabled. No Client rule duplication or new Alert UI is needed.
