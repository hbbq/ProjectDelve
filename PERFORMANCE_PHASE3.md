# Performance Phase 3 — Patrol and Normal pursuit

## Result and scope

Phase 3 removes repeated full-map Patrol searches and unnecessary hypothetical
attack-origin evaluations. No gameplay distance limit, behavior policy, Order,
transport, rendering, animation or persistent GameState cache changed.
CONSTRAINTS.md is unchanged. Phase 1 placement/Summon and Phase 2 publication
implementations are untouched.

On the exact saved 80x80 world, the reproducible Engine sequence fell from
2,878 ms to 69 ms for Patrol and from 2,213 ms to 208 ms for the Normal diagnostic.
Allocated bytes fell from 736 MB to 32.5 MB and from 2,832 MB to 75.5 MB respectively.
These are Release p50 measurements, not browser timings. All compared full trace
digests match, including candidates, paths, events, event-time FoW observations,
RNG calls and save/continue.

## Fixture and reproducibility

The exact provided DELVE1 is retained at
`.tmp/world80-investigation/original.delve`, SHA256
`b1c55c3953473346ac119f56e42f30df08e4f2919fa7e82f7ad86bb0d64c09d7`.
It has Beastmaster at (3,31), five Awake Road Patrol Grunts, 71 Dormant monsters,
Hero-side FoW, and Loot preset All. Positions, Groups, controllers, Orders and
FoW were preserved. Only the diagnostic Normal variant replaces Patrol Orders.
It is not a regenerated approximation and is not a test fixture.

The original and Normal states were materialized against detached copies of the
local Canonical publication and Workspace, then serialized once for all stages.
The effective catalog supplies 32 Loot Cards; compiled C# content alone does not
resolve the saved All deck equivalently. Publication SHA256:
`0f8699524bdeb871abd546cc76cac8e663b943204496e5535f56f3277286e6ba`;
Workspace SHA256:
`35d9d10ebbbcb321a256280b87b8ced767d81ee361cadc91bd0767c8546d5fb5`.

Baseline assemblies were frozen at commit
`5c001926bd76d78cad3e6f798ad19c81b4e15ea6` before production edits. Phase 3A was
frozen before wiring the Phase 3B helper into approach queries. See the
[benchmark instructions](tools/PathfindingBenchmark/README.md) and
[machine-readable results](tools/PathfindingBenchmark/phase3-results.json).
Temporary raw logs, authoritative snapshots and separate instrumented copies are
under `.tmp/phase3`; they are not production dependencies.

Windows, .NET 10.0.12, 16 logical processors, workstation GC, default tiered JIT.
Release builds and heavy workloads ran sequentially. The baseline uses one
warmup and three samples; each after stage uses three warmups and ten samples.
All have a one-second JIT settling interval outside timing. p95 uses nearest
rank; baseline p95 is just the maximum of three samples, not a tail estimate.
No Debug performance claim is made.

The timed exact-world sequence is Start Round to first Human input, Stay, End
Turn through pending automation, then Start Round to the next Human input.
Seed 1 draws Patrol before Beastmaster; seed 0 exercises Beastmaster first and
Patrol again at the following Round Start. The table below uses seed 1. It
therefore includes a little more work than the investigation's initial-only
2,794/2,233 ms intervals. Generation, materialization, JSON traces and observations
used only for digest verification are outside timing. Provider clocks exclude
candidate generation/resolution; allocation and GC columns cover the complete
timed Engine sequence.

## Phase 3A algorithm

`RouteDistanceToArea` first performs forward BFS with precisely the existing
directed `CanStep` relation. It returns zero on initial any-footprint overlap and
stops at the first reachable overlapping anchor. Unit-cost BFS discovery order
makes this the same minimum as the former full flood followed by a minimum scan.
It has no special Flee endpoint exception, new occupancy rule or new movement
graph. Legal Move candidates and their canonical paths are unchanged.

After 256 expansions, the query switches algorithms: it completes the exact
answer with one reverse multi-source distance field, shared by subsequent
queries for that actor, waypoint area and door policy in the same detached
GameplayQueries snapshot. **256 is a computation budget, not a distance or
reachability limit.** A 599-step route is tested as reachable. No result is
declared unreachable merely because that budget is exceeded.

Reverse edges test `CanStep(predecessor, current)`, preserving asymmetric entry
restrictions. Goals include every on-board anchor overlapping the waypoint,
even an initially unknown/restricted anchor: the old forward flood admitted
its origin unconditionally. Traversal still prevents entering invalid goals.
Multi-cell any-overlap goals are separate from guard-return whole-footprint
goals. Cache keys and query ownership prevent reuse across changed actors,
capabilities, knowledge, geometry, areas or route progress. No cache is serialized.

Algorithm comparison on an 80x80 board, 32 origins, five passes, Release medians:

| Workload | Forward visited / ms | One reverse field visited / ms |
| --- | ---: | ---: |
| Nearby | 971 / 1.31 | 6,400 / 6.13 |
| Distant | 201,664 / 125.30 | 6,400 / 3.97 |
| Unreachable | 102,400 / 71.92 | 3,200 / 1.51 |

All distance vectors matched. This motivated the hybrid rather than always
constructing a global field or retaining repeated long forward searches.

## Phase 3B algorithm

The detached query captures known, living, hostile, placed target IDs in their
original Unit order. General attack-origin queries no longer invoke the target
query for every friendly, Dormant friendly, dead or unknown Unit at every cell.
Dormancy itself is not a reason to exclude a hostile target.

For currently supported direct attacks, possible origins are the union of
target-cell neighborhoods minus actor-footprint offsets. The bound includes
effective RNG, fixed TargetUnitAttack RNG and Claw adjacency; range-one diagonal
Melee remains included alongside larger ranges. Board clipping uses widened
arithmetic, duplicates are removed, and anchors are evaluated in the original
Y/X order. Every retained origin still passes the original placement and exact
attack evaluator. There is no ATK-based pruning: Fury can make a future origin
attack-capable even when current ATK is zero.

Fireball's indirect targeting and any snapshot Unit/Equipment declaration with
a positional adjacent RNG modifier retain exhaustive origin enumeration. This
includes negative auras, inactive declarations and exhausted grants: the
fallback is deliberately conservative. New attack mechanics must supply a
proven conservative bound or explicitly retain that fallback. Exact evaluators
continue to decide LOS, source/target geometry, posture, grants, charges and
attack dice. The normal-versus-explicit-target distinction for Phase and
traversal-only origins is unchanged. Reverse approach traversal continues to
ignore figures and Cell Markers as before; actual legal movement does not.

Reducing the number of hypothetical origins removes most state copies without
changing GameState.Copy or replacing positional-stat evaluation. Existing
query-local target/origin reuse remains intact. Action selection, summoner
following, explicit Orders, congestion Stay, retreat and movement patterns
continue through their existing paths.

## Exact-world results

| Measurement | Before | After 3A | After 3B |
| --- | ---: | ---: | ---: |
| Patrol Engine p50 / p95, ms | 2,878 / 2,891 | 71.7 / 75.4 | 69.3 / 70.7 |
| Normal Engine p50 / p95, ms | 2,213 / 2,241 | 1,772 / 1,852 | 208 / 225 |
| Patrol allocated MB/run | 736.3 | 37.8 | 32.5 |
| Normal allocated MB/run | 2,832.4 | 2,829.6 | 75.5 |
| Patrol median GC gen0/1/2 | 86 / 27 / 3 | 4 / 0 / 0 | 4 / 0 / 0 |
| Normal median GC gen0/1/2 | 338 / 24 / 0 | 338 / 30 / 1 | 9 / 3 / 0 |
| Patrol route-distance queries | 123 | 123 | 123 |
| Patrol full forward floods | 123 | 0 | 0 |
| Patrol forward dequeued cells | 640,584 | 3,179 | 3,179 |
| Normal attack evaluations | 27,025 | 27,025 | 549 |
| Normal target-query calls | 2,051,280 | 2,051,280 | 164 |
| Normal state copies | 26,674 | 26,674 | 198 |
| Normal reverse approach fields / visited cells | 5 / 27,960 | 5 / 27,960 | 5 / 27,960 |

Counts come from separate temporary instrumented initial-resolution runs, not
the timed binaries. Inclusive profile time/allocation rows overlap and are not
summed. The earlier investigation's roughly 709 MB was provider allocation;
this report's 736 MB baseline includes the complete reproducible Engine sequence.
Patrol visits fall 99.50%; Normal attack evaluations fall 97.97%, target-query
calls 99.992%, and copies 99.26%. Full-run allocation reductions are 95.59% and
97.34% respectively.

Phase 3A does not optimize Normal: its operation counts remain identical. Its
lower exact-world time and some higher large-world times reflect JIT/runtime/GC
variation and different warmup counts, not a claimed Normal improvement.

A separate rerun of the investigation's interval probe measured final Patrol
initial resolution at 78.7 ms (72.9–84.7) and post-Beastmaster resolution at
186.7 ms (158.4–190.0); Normal measured 205.2 ms initial and 210.7 ms post.
These three-repeat interval diagnostics have no JIT settling pause and exercise
different process-warmup positions, so they are not substituted into the primary
warmed table. Their raw samples and the corresponding 3A intervals are retained
in `separateIntervals` in the results JSON. Both reported stalls are substantially reduced
without relying on favorable token ordering.

## Generated-terrain scaling

These are benchmark-owned two-Unit controls on generated terrain, not assertions
about mutable authored worlds. Each size has the same terrain across phases.
Near/far goals share a passable component; unreachable goals are separated by a
benchmark-authored rock barrier. Component sizes differ with generated terrain,
so board area alone does not predict cost. All figures below are p50 milliseconds;
every p95, allocation, GC count and raw sample is in the results JSON.

| Board / planner | Nearby before → after | Distant before → after | Unreachable before → after |
| --- | ---: | ---: | ---: |
| 30x24 route | 4.14 → 0.39 | 3.82 → 0.79 | 2.47 → 2.32 |
| 30x24 Normal | 0.49 → 0.40 | 3.27 → 0.70 | 2.88 → 0.49 |
| 60x50 route | 5.93 → 1.04 | 6.06 → 1.40 | 5.82 → 0.83 |
| 60x50 Normal | 0.81 → 0.73 | 12.68 → 1.19 | 26.51 → 0.70 |
| 120x120 route | 532.22 → 2.56 | 524.89 → 133.72 | 273.76 → 35.41 |
| 120x120 Normal | 3.19 → 2.51 | 385.69 → 94.48 | 332.41 → 32.24 |

There is no universal speedup claim. Small nearby Normal queries already avoid
global approach fields. The 30x24 unreachable route gains little and allocates
4.10 MB versus 3.62 MB before. Large distant/unreachable graphs still need exact
reverse traversal; long-range or numerous targets can cover much of the board.
Fireball and positional-range-aura cases intentionally remain exhaustive.
Terrain/edge lookup and full reverse traversal are remaining costs, especially
on 120x120; no rendering or global-state redesign was attempted.

## Equivalence and verification

57 new dedicated Engine cases supplement existing route, attack-query, approach,
Order, movement, FoW, summon and publication suites. Independent full-flood
oracles cover near/far/unreachable areas, ties, partial 2x2 overlap, doors/walls/
internal edges, Water/Lava, Phase/Amphibious, unknown geometry, directed origin
exceptions, field reuse, snapshot isolation and forced fallback. Attack tests
cover diagonal Melee, footprints, ranged/fixed/Claw/FireBreath grants, mixed
ranges and charges, Fireball fallback, positive/negative/equipment RNG auras,
Fury with zero current ATK, hidden/no targets, local definitions and save/load.

All 20 benchmark trace digests match baseline after both stages. The exact
Patrol and Normal variants additionally match under seed 0, including automation
after Beastmaster and the following round. Every trace matches its own
serialize/deserialize continuation. These are full results and event-time FoW
observations, not merely selected destinations. The first Patrol sequence remains
unit-3, unit-4, unit-2, unit-1, unit-0, with destinations (48,37), (48,38),
(48,39), (48,41), (48,42), unchanged waypoint/Loot events and RNG consumption.

Release solution build succeeded. All 3,133 Engine tests passed. Web: 738 passed,
12 failed with existing `knightly-boots` conflicting-content identities. Replacing
Engine/Generation/Web assemblies with the frozen baseline reproduced the same
12 failures (the affected class has four other passing cases). No unrelated test
or content definition was altered. All 1,016 Node client/security tests passed
after selecting the documented installed Python/Pillow runtime; the default
Python shim initially failed model-preparation subprocesses. NuGet vulnerability
feed access produced a cached-restore warning, not a build failure.

An independent read-only review found no correctness defect. Its requested
Amphibious-water and mixed-range grant coverage was added and passed.

## Edge verification

An isolated Release host on 5081 used the exact saved payload and detached
content. The existing 5080 process remained untouched. A separately reviewed
temporary Playwright script exercised Patrol and Normal on 1440x1000 desktop
and 390x844 mobile: import, Round Start, Beastmaster Stay/End Turn, following
Round Start and Human readiness. Desktop/mobile screenshots were inspected.
No page errors, console errors, failed requests or HTTP errors occurred.
Hidden Road Patrol Units and their events were absent from Hero-side playback;
authoritative trace comparison verifies their actual moves and shared waypoints.

First cold Patrol start was 2,032 ms (previous investigation: 5,447 ms). Warm
initial starts were 1,322–1,330 ms and following Round Starts 1,311–1,340 ms.
End Turn was 675–736 ms. These are small browser samples, not p95 claims.
The existing 400/520/300 ms initial pauses and 280/350 ms completion pauses
remain unchanged and now dominate visible waiting. Engine and playback overlap;
their durations must not be simply added. The isolated host was stopped after QA.
