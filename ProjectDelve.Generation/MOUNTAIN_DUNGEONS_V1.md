# Mountain Dungeons V1

Mountain Dungeons are ordinary Civilization Sites carved into remaining Solid Rock.
They share the world Board and all existing movement, Door, terrain, FoW, Unit,
Group, activation and Loot rules. No standalone DungeonGenerator is invoked.

## Pipeline and implementation

The top-level WLD1 schedule is unchanged. Civilization internally plans Mountain
Dungeons before Settlements, Campsites and Beacons, after primary routes. Dungeon
targets are independent of settlement targets:

```
L = max(CampsiteDensity, SettlementDensity) // None=0 .. VeryHigh=4
E = floor(sqrt(width * height * 720))
target = min(4, ceil(E * L / 2400))
```

Disabled Civilization or two None densities request none. Targets are requests;
the generator never changes natural terrain parameters to guarantee placement.
At most one Site uses each cardinally connected rock mass measured before its
excavation. Existing enrichment and route excavations split those masses; original
analyzer formation IDs are provenance, not suitability boundaries.

`MountainDungeonGenerator` surveys writable rock, an eight-neighbor interior core,
packed minimum room capacity and entrances bordering the principal playable
component. It considers at most 24 masses and five attempts per mass/request,
within Civilization's existing 512-attempt / 2,000,000-work allowance. Each room
tries at most 72 ranked centers with a small descending size list. Failed child
drafts publish nothing. Final publication uses the existing authoritative
`PlanningBoard.TryCommit` validation; room searches use local masks and bounded
floods rather than repeated Engine scenario validation.

One room-and-corridor carver has three profiles:

| Profile | Geometry preference |
| --- | --- |
| Tunnel Network | Smaller chambers linked from the preceding room, with blind spurs and occasional extra links |
| Chamber Complex | Distributed larger chambers and branches; room fallback stops at 3×5 |
| Deep Passage | Rooms chosen farther inward, connected to the existing excavation with side branches |

Every accepted layout has at least two rooms. Requested rooms range from 3×5 to
5×7 with deterministic rotation; Tunnel/Deep can shrink to 3×4 or 3×3 when needed.
Some larger rooms have clipped corners. Most corridor steps gain a parallel lane
when the shell permits; entrance necks and constrained steps can remain one Cell
wide. Room size, position, profile and connecting paths vary with the layout
stream; optional features use a separate stream.

The patch excavates ordinary Stone Floor and reserves both excavation and local
remaining rock shell, not the whole mountain. Entrance metadata uses SitePortal
and SiteAccessPort. Route permissions stop at the outward approach and never
grant traversal or writing through the dungeon. No branch connection is required
because the approach already belongs to the principal world component. Existing
road contact is still reported by route topology. Optional internal or entrance
Doors use ordinary EdgeKind.ClosedDoor.

## Features, occupation and access

Optional Lava is a small connected pool of one to three Cells using existing
`ordinary-lava` (Coal generation Off). Each accepted addition preserves access
through all remaining floor and at least one complete 2×2 placement per room.
Required spanning access from the entrance through every room is kept free of
Lava and population. This does not promise 2×2 passage through the whole dungeon.

Treasure metadata is optional. After final geometry, each treasure area has a
three-in-four chance to supply one to three ordinary Loot markers when Loot is
configured and legal space exists. This path runs even with Population absent or
Off. Empty/custom finite supplies retain their existing limits; preset decks use
the existing provisioning function. Ordinary Population Loot remains separate,
deduplicates occupied marker Cells, and uses the same finite supply.

Site Population Intent now supports independent encounter regions. Dungeon rooms
publish bounded region sizes, existing Monster composition preferences and
optional room-local patrol paths. A whole Site may be vacant; individual regions
may also be skipped. Occupants use the existing difficulty-weighted Monster pool,
excluding summoners from initial composition. Eligible large chambers may request
the existing Red Dragon at difficulty five. Complete footprints must fit inside
the region and avoid required access, including the exterior entrance Cell.

The shared World Population pool includes published `demolition-imp` when available
in effective content, at half Bomb Imp's selection weight at every Difficulty.
Room preferences include that reference, and ordinary group sizing can place
multiple instances. No room or dungeon is guaranteed one. Its existing ordered
defeat consequences can destroy dungeon geometry during gameplay; construction
reservations do not restrict that destruction. See [Population V2](POPULATION_V2.md).

Occupants within a region form a connected initial Alert component. Distinct
encounters must remain separate in the full initial Alert topology, including
existing Units and awake relays; Walls and Doors do not supply isolation. Most
Units are Dormant; sentries and optional short room-local patrols use ordinary
Orders and wake settings. Larger Units receive guard occupation, never a patrol
that assumes they can leave the chamber. Runtime movement can subsequently
change Alert connectivity, exactly as before.

`PlayableWorldComponent` is the shared ephemeral doors-openable geometry query
for dungeon entrances and Quick Play. It includes Machine blockers but ignores
figures when choosing the largest component, breaking equal-size ties by the
top-left Cell. Each dungeon retains that selected region's top-left Cell in
`MountainDungeonInfo.AccessRootIndex`. Final geometry checks every dungeon entrance
and usable room Cell against the region connected to that root after later routes.
It still rejects a blocked root or real disconnection; later growth of another region
does not reassign the root. Quick Play independently selects the final largest region and applies its existing free
five-Hero compact cluster/open-Edge rule only within the chosen component, and
fails clearly if no compatible cluster remains. It never falls back to a small
isolated pocket merely because its Cells occur earlier in board order.

The Lake Country 50×50, seed 13, offset (-17,-13) regression retains its original
48-Cell region, expanded to 165 Cells including its dungeon, while Civilization
creates a separate 423-Cell region. This is valid anchored access. The final-largest
Quick Play policy still selects the 423-Cell region. See
[Accessibility Analysis V1](ACCESSIBILITY_ANALYSIS_V1.md) for advisory actor-specific queries.

## Authoring and visualization

`SiteInfo.MountainDungeon` holds real RoomInfo entries directly, corridor spans,
protected rock, required access, Lava, treasure, rock mass identity/area and room
capacity. No fake roofed Buildings are created. Civilization diagnostics report
requested/accepted/omitted dungeons, preparation and failures; stage previews
include rooms, profile, shell, encounter regions and independent Site Loot.
Construction operations retain the ordinary before/after terrain and Edge replay.

Enhanced 2D uses existing rock and floor rendering. Tabletop 3D already places
raised rock tops/walls in its cutaway obstacle layer; its flat known rock base
remains at floor height. Tests and browser inspection verified that cutaway
exposes the interior without revealing unknown terrain. Consequently no new
rendering system or production cutaway change was necessary. Generator metadata
stays in authoring previews; gameplay receives ordinary FoW-filtered projections.

## Verification and measured corpus

See the measured acceptance table below. Local reproducible investigation artifacts
are in `.tmp/mountain-v1`: `Program.cs`, `results.jsonl`, all concrete layout JSON
files, `analyze_corpus.py`, `layout-examples.png`, and `benchmark.json`.

Focused tests cover thick/thin masses, split remaining rock, unreachable entrances,
profile/seed variation, widths, preparation limits, rollback, protected routes and
Edges, Lava, supplies, Population Off, independent Alert regions, complete Unit
footprints, current-format transport/save state and Quick Play component policy.
Existing Engine/Web replay and FoW suites continue to exercise the unchanged
runtime rules. Client tests cover direct Site room inspection, stage-local Loot,
room counts and cutaway under FoW.

Browser acceptance used the protected repository dispatcher and an isolated 5081
host. It covered a generated Rocky Wilds 60×50 seed-3 preview, construction/room
overlays and diagnostics, real Quick Play with its ordinary initial FoW, and a
temporary cropped copy of a generated interior with the canonical Heroes inside
to inspect Tabletop 3D before/after cutaway. The crop is a QA fixture, not a
generator map transition or a Quick Play starting rule. Console checks reported
no application errors or failed requests.

## Limits and deferred work

- Small/default geography can legitimately omit every requested dungeon. No
  terrain-generation overhaul or guaranteed occurrence is included.
- V1 publishes one entrance per dungeon. Secondary entrances remain optional
  future work; there is no requirement to use a dungeon as a world through route.
- Profiles share the bounded carver rather than separate frameworks. Dungeon
  layouts currently stop at five rooms and local spurs/optional links.
- Large creatures may remain in their chamber. No large-Unit rescue, movement or
  activation exceptions are introduced.
- Initial independent occupation can be reduced or omitted when Alert spacing,
  required access or the global Population allowance leaves insufficient room.
- Work counters are deterministic planning allowances, not precise CPU-time
  accounting. Wall-clock measurements are environment-dependent; full scenario
  publication remains a substantial part of generation cost.
- Historical WLD1/DELVE1/saved-game compatibility is not promised. Current-format
  determinism and serialization remain mandatory.

## Measured acceptance corpus — 2026-11-11

56 worlds: eight seeds per configuration. Seeds (hex): `1`, `2`, `3`, `2A`,
`3039`, `123456789ABCDEF0`, `DEADBEEF`, `C0FFEE`. Default terrain uses
35% Forest / 12% Water / 15% Rock, Balanced/Auto. Rocky Wilds uses
15% / 8% / 50%, Broad/Mixed. All use default enrichment, Low Campsites,
Medium Settlements, Low through/exploration routes, Medium Population/difficulty 3,
and All Loot; route profile is MinorPath for Default and RockPassage for Rocky Wilds.

| World | Accepted / requested | Rooms | Lava dungeons | Treasure areas | Generation median / range (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 30×24 Default | 1 / 8 | 2 | 0 | 0 | 96 / 81–211 |
| 60×50 Default | 4 / 16 | 8 | 2 | 4 | 597 / 497–889 |
| 60×50 RockyWilds | 15 / 16 | 43 | 5 | 10 | 588 / 413–923 |
| 80×80 Default | 13 / 16 | 29 | 4 | 9 | 856 / 628–1279 |
| 80×80 RockyWilds | 16 / 16 | 61 | 5 | 11 | 874 / 814–955 |
| 120×120 Default | 21 / 24 | 47 | 5 | 11 | 3038 / 2839–3400 |
| 120×120 RockyWilds | 24 / 24 | 80 | 4 | 15 | 3284 / 3004–4463 |

Final corpus: **94 accepted / 120 requested**, 270 rooms, 25 dungeons with Lava,
60 treasure areas and 87 Site treasure markers. Profile distribution: 41 Tunnel
Network, 31 Deep Passage, 22 Chamber Complex. 27 rooms are 3×3; the remaining
243 vary through rotated 3×4, 3×5, 3×6, 3×7, 4×5, 4×6, 4×7, 5×5, 5×6 and 5×7
bounds, with some clipped corners.

Corridor local cross-section counts: **147 one-Cell, 1682 two-Cell, 806 three-or-more-Cell**.
These count each corridor Cell by the shorter horizontal/vertical contiguous
floor span, capped at 3; junctions and room mouths can be wider. A separate
complete-2×2-floor measurement covers 2431 of 2635 corridor Cells. This measures
local maneuvering width, not universal 2×2 reachability from outside.

Every accepted entrance and usable room Cell remained in the final principal
component. Every protected shell Cell remained Solid Rock. Every Quick Play start
used the principal component. All 56 DELVE1 round-trips passed; fixed recipes
for seeds 1 and 2A were regenerated across all seven configurations with equal
fingerprints. Omission reasons: 19 insufficient room/core geography, seven no
legal main-component entrance. No partial construction or forced terrain repair
was accepted.

67 of 94 dungeons were occupied, with 130 independent room encounters and 252 occupants.
The rest stayed unoccupied. Ordinary difficulty-3 dungeon compositions included
bomb-imp-type, displacer-demon-type, ghost-type, goblin-type, grunt-type, skeleton-archer-type, troll-type, zombie-type. 3 occupants had ordinary awake patrol roles.

At least 53.4% of each accepted original rock mass remained unexcavated immediately after dungeon planning (median 77.3%).
Later stages may develop unreserved distant mountain cells; the local shell itself
is the strict preserved boundary.

Concrete larger examples (full recipes are retained in the corpus JSON):

| Configuration / seed | Profile | Room dimensions | Corridor Cells | Preserved shell Cells |
| --- | --- | --- | ---: | ---: |
| 80×80 RockyWilds / `0000000000003039` | TunnelNetwork | 4×7, 3×5, 3×5, 3×4 | 26 | 65 |
| 60×50 RockyWilds / `00000000DEADBEEF` | ChamberComplex | 5×5, 5×6, 5×4, 5×6, 5×6 | 97 | 192 |
| 80×80 RockyWilds / `0000000000000001` | DeepPassage | 4×6, 5×6, 4×6, 6×5, 5×5 | 52 | 137 |

Visual inspection covered generated room/corridor crops and the browser preview.
The initial corpus had 240 of 291 rooms at 3×3, so fitting was changed to try
larger rooms at multiple positions before shrinking, then room orientation was
varied. The final corpus above supersedes those initial quality measurements.

Single-request planning probes used the post-primary-route board, seeds 1 and 2A
for each configuration, and the normal preparation policy. They include rock
survey, suitability, layout, local connectivity checks and draft preparation.
Publication separately includes the existing authoritative Engine validation.

| Size | Planning range (ms) | Accepted publication range (ms) | Maximum charged work |
| --- | ---: | ---: | ---: |
| 30 | 0.7–9.9 | No accepted request | 8,640 |
| 60 | 4.3–13.8 | 15.3–25.6 | 42,360 |
| 80 | 5.3–64.9 | 36.9–58.6 | 116,872 |
| 120 | 12.4–17.5 | 33.2–52.8 | 186,674 |

These are local Debug-build wall-clock measurements, including warm-up effects;
they are not an isolated release throughput benchmark or a claim of fixed overhead.
Total generation timings above include all world stages and final materialization.
Population timing is already recorded separately in `results.jsonl.stages`;
each planning probe excludes population. Failed plans do not pay publication
validation cost. No persistent geometry cache was introduced.

## Final verification and changed files

The final solution build passed. Full verification on the final implementation:

| Suite | Result |
| --- | --- |
| Engine (.NET) | 3,256 passed, zero failed |
| Web (.NET) | 760 passed, zero failed |
| Browser client and dispatcher (Node) | 1,033 passed, zero failed; one optional benchmark skipped |

The complete 94-layout contact sheets were visually reviewed in addition to the
three concrete examples above. Compact two-room sites remain common in Default
terrain; Rocky Wilds also produces longer branching complexes with three to five
rooms. The three profiles are preferences within the same geography-constrained
algorithm, so small sites can look similar across profiles.

Mountain Dungeon changes are grouped below; earlier Friendly Swap changes in the
working tree are separate.

| Responsibility | Files |
| --- | --- |
| Layout and Site metadata | `MountainDungeonGenerator.cs`, `CivilizationModels.cs`, `CivilizationGenerator.cs` |
| Shared accessibility and route attachment | `PlayableWorldComponent.cs`, `RouteNetworkMetadata.cs`, `ProjectDelve.Web/WorldQuickPlay.cs` |
| Population, independent Loot and pipeline | `DungeonSitePopulation.cs`, `SitePopulationIntent.cs`, `PopulationModels.cs`, `PopulationGenerator.cs`, `PopulationPlacement.cs`, `WorldGenerationPipeline.cs` |
| Designer | `ProjectDelve.Web/wwwroot/world-generator.js`, `world-generator-state.js` |
| Engine tests | `MountainDungeonTests.cs`, `DungeonPopulationTests.cs`, `MountainDungeonIntegrationTests.cs`, `CivilizationGenerationTests.cs` |
| Web/client tests | `WorldQuickPlayTests.cs`, `world-generator.test.cjs`, `three-viewport.test.cjs` |
| Documentation | `CONSTRAINTS.md`, `DENSITY_SCALING_V1.md`, `WORLD_GENERATOR.md`, this report |

Unqualified production paths above are in `ProjectDelve.Generation`; test paths
are in their corresponding Engine/Web test projects.
