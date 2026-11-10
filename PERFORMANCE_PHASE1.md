# Performance Phase 1: bounded placement queries

Measured on 2026-11-10, .NET 10, Windows, Release unless stated otherwise. This
implements the bounded-query phase of the performance investigation. It changes
Engine discovery and lookup work, not game rules, content, transport or state ownership.

The subsequent Web detachment/publication changes and current measurements are
documented in [PERFORMANCE_PHASE2.md](PERFORMANCE_PHASE2.md). This report retains
the original Phase 1 measurements.

## Result

The severe Beastmaster pause was caused by whole-board Summon discovery plus
repeated linear Unit/Type/Figure resolution and footprint expansion. On the fixed
120x120 fixture with 100 distant Dormant Grunts, post-Move Engine resolution fell
from 19.27 seconds to 4.06 ms and Summon Raptor submission from 18.84 seconds to
3.55 ms. The isolated Summon query allocates 154 KB instead of 4.73 GB.

Saved 442-monster Quick Play also improves substantially through cheaper placement
and edge validation. Browser preparation/import fell from 43.95 to 15.20 seconds.
Serialization of detached Web responses now dominates that remaining delay.

## Implementation and ownership

- `ProjectDelve.Engine/SummoningRules.cs` bounds discovery using occupied source
  Cells and the receiving footprint's offsets. For rectangular footprints, the
  minimum anchor is source-minimum minus one minus the receiving maximum offset;
  the maximum is source-maximum plus one, clipped to board anchors. A 1x1/1x1
  query checks a 3x3 superset (nine anchors; occupancy rejects the center). A 2x2
  recipient can start two Cells left/above a 1x1 source. No eight-anchor assumption
  is applied to larger footprints.
- Existing placement, receiving-Type terrain permission and geometric adjacency/
  LOS predicates still decide legality. Loops remain Y then X, with existing
  Ability order, keys and source binding. Population limits, provenance, ID
  allocation, counters and RNG use are unchanged. Unindexable malformed state
  retains exhaustive discovery and the legacy ordered occupancy scan.
- `ProjectDelve.Engine/PlacementLookup.cs` lazily resolves Unit and Type IDs once
  and expands each Figure footprint into Cell -> occupant IDs. Multiple occupants
  are retained, including invalid overlaps, so exclusion of one Figure cannot
  hide another. Physical occupancy does not disappear merely because HP or
  Posture changes. Invalid references/duplicate Figure identities fall back to
  the old scan; focused tests check short-circuit/exception behavior.
- This is an **internal, unchanged-state query object**, never a GameState field
  or serialized data. There is no version tracking or automatic invalidation.
  Discard it before mutation. Public placement/edge calls create fresh contexts;
  separate iterator enumerations also create fresh contexts. Callers must not
  mutate state during one enumeration or pass a context to another state.
- `SpatialRules` retains `Fits` before occupancy checks. Board terrain, machines
  and edges remain live authoritative reads; this phase adds no board cache.
  `GameEngine.ValidateScenario` shares one context across final placement and edge
  checks. `EdgeReplacementRules.ReplaceEdges` shares one while evaluating all
  replacements before mutation. Public `PlaceUnit` uses fresh lookup data; no
  incremental creation API was needed.
- `AbilityExecution.ActiveAbilityCandidates` shares one lazy placement context
  across Summon Abilities in the actual activation path. The standalone Summon
  enumerator also shares one across its Abilities. Hypothetical states get their
  own contexts; nothing is cached on publicly mutable GameState.
- `GameEngine.ActionCandidates` validates the actor's unchanged placement once
  before target enumeration. Each target still runs the original range, LOS,
  target legality and passive-stat evaluation. GameplayQueries' existing caches,
  authoritative Advance validation and Bonus Action relevance logic remain intact.

Lookup storage/build work is proportional to Units, Types and occupied footprint
Cells, not board area. Single placement calls now build O(N) lookup data instead
of repeatedly resolving each Figure through O(N) searches. Sequential creation
still rebuilds per insertion (approximately quadratic aggregate work); that is
deliberately simpler than keeping mutable setup indexes synchronized. Query
contexts are private derived data, not physical gameplay state, and expose no new
authoritative hidden information to clients or Automated decision providers.

## Reproduction and measurement boundaries

Use [the benchmark harness](tools/PlacementQueryBenchmark/README.md). It supports
fixed synthetic fixtures, warmups, raw JSONL samples, p50/p95, allocations, GC
counts, isolated candidate queries, Engine Advance, creation, Dormant and Quick
Play stages. Optional frozen DLL references allow the same harness to run against
the pre-change Engine/Web/Generation binaries. No permanent Engine instrumentation
or performance assertions based on wall-clock time were added.

Original Release binaries were frozen before implementation in `.phase1-baseline`.
Fresh comparison measurements used the original `.tmp/perf-probe/Program.cs` on
both binary sets. These stage samples include the first invocation; their tables
below use the median and nearest-rank p95. The new harness additionally ran three
warmups and 30 samples. Its emitted p50 uses nearest rank. Do not compare different
harness stages as identical end-to-end operations.

Fixtures:

- Small: 12x12, Beastmaster (2,2), Dormant Grunt (8,8), no distant monsters.
- Large controls: 120x120, same nearby setup, plus 100 or 442 distant Dormant
  Grunts at `(15 + i % 100, 15 + i / 100)`. Counts are **distant additions**;
  total Units are 102 and 444. Both controllers are human, blue FoW, no objective,
  empty Loot. Quiet Round Start substitutes Rogue. Each Advance starts from the
  same state, selects Stay to complete Move, or the first Raptor placement.
- Quick Play: saved generated world
  `assets/previews/population-v2-442-study.delve`, SHA256
  `22E99A8AF8F87F3E06E6267BD1851FD54F484E641EE46C5960E956B52F50DE70`.
  It contains 442 generated monsters, 4,244 terrain entries and 136 edges before
  preparation. It is optional benchmark input, not a behavioral test fixture.
- Small Quick Play uses the benchmark-owned small definition. No layout assertion
  depends on mutable authored scenarios.

Heavy measurements ran sequentially, without concurrent builds/tests. Allocations
are allocated bytes, not retained heap size; the harness uses process-wide precise
GC counters. JIT tiering, OS scheduling and process history affect timing and
allocation distributions. The machine's existing 5080 host was left untouched.
The expensive original 442-distant activation measurements are the investigation's
retained single samples; they were not repeated. Three-sample baselines do not
support a reliable tail estimate. Reported gains below are measurements, not
predictions for every generated world.

Local raw evidence is in `.tmp/perf-probe/phase1/`; older evidence remains in
`.tmp/perf-probe/`. [Aggregate results](tools/PlacementQueryBenchmark/phase1-results.json)
retain the measurements needed for comparison without committing temporary source
copies, instrumentation or browser hooks.

## Release timings and allocations

Engine Advance only; excludes HTTP, Web publication and animation. MB/GB are decimal.

| Fixture / operation | Before p50 ms | After p50 / p95 ms | Before -> after allocated bytes | Samples before/after |
| --- | ---: | ---: | ---: | ---: |
| Small, post-Move | 3.253 | 0.352 / 1.704 | 2.152 MB -> 0.282 MB | 30 / 30 |
| Small, Summon Raptor | 2.067 | 0.373 / 0.760 | 2.164 MB -> 0.294 MB | 30 / 30 |
| 100 distant, post-Move | 19,269.537 | 4.059 / 4.437 | 4.712 GB -> 1.161 MB | 3 / 30 |
| 100 distant, Summon Raptor | 18,839.361 | 3.545 / 9.423 | 4.768 GB -> 1.339 MB | 3 / 30 |
| 442 distant, post-Move | 260,266.451* | 34.286 / 49.783 | 18.847 GB -> 4.034 MB | 1* / 30 |
| 442 distant, Summon Raptor | 261,528.387* | 19.752 / 28.832 | 20.172 GB -> 3.427 MB | 1* / 30 |

`*` Historical single sample, not a population median. Fresh before p95 for the
small Move/Summon cases was 5.310/3.322 ms. The maximum of the three 100-distant
samples was 19,299.613/19,090.713 ms; it is not a robust p95.

The independent warmed harness gives post-Move/Summon p50 of 3.93/3.41 ms for
100 distant and 22.15/18.39 ms for 442 distant. Their p95 values are 5.82/3.76 and
31.48/25.43 ms. These corroborate the improvement, while showing tiering/process
history effects rather than implying one exact universal latency.

The isolated 100-distant **Summon candidate query**, including discovery, limits,
placement/terrain/LOS checks and materialization, fell from 13,183.66 ms and
4,725,442,088 bytes (three samples) to 0.182 ms median, 0.279 ms p95 and 154,176
bytes (three warmups, 30 samples). Both returned 40 identical candidates. This
allocation figure covers the entire query, not only the placement predicate.

Temporary instrumentation of copies of the source measured:

| 100-distant operation | Before CanPlace calls | After CanPlace calls | After lookup builds |
| --- | ---: | ---: | ---: |
| Post-Move Advance | 72,103 | 47 | 3 |
| Summon Raptor Advance | 72,103 | 47 | 3 |

Five Summon Abilities account for 72,000 -> 45 of those checks. The after profile
measured 127,568/127,744 bytes inside CanPlace and 132,696/132,936 bytes inside
lookup builds for Move/Summon respectively. These inclusive scopes overlap and
must not be added. Instrumentation itself allocates, so whole-operation allocation
comparisons use uninstrumented runs above. No isolated before-predicate allocation
measurement was captured; the complete candidate-query comparison is the reliable
before/after allocation measure.

Debug control, 100 distant: historical first-invocation Move/Summon was
30,381/31,147 ms. Current 30-sample medians are 4.39/3.91 ms, p95 6.77/6.42 ms.
These use different sample counts and are corroboration, not the primary comparison.

## Quick Play and Dormant controls

Saved 442 world, original probe, three samples per stage:

| Stage | Before median ms | After median ms | Before -> after allocations |
| --- | ---: | ---: | ---: |
| Preparation input parse + validation | 2,966.47 | 145.91 | 171.48 -> 51.59 MB |
| WorldQuickPlay.Prepare | 9,040.95 | 339.10 | 345.78 -> 110.46 MB |
| Transport encode | 4.32 | 13.56 | 3.32 -> 3.32 MB |
| Standalone prepared CreateGame control | 6,272.05 | 201.23 | 170.44 -> 53.82 MB |
| In-process Web import | 31,681.57 | 19,457.14 | 1,089.64 -> 956.09 MB |

The sums of preparation stage medians are 12.01 seconds before and 0.50 seconds
after; these are not a separately measured combined-operation percentile. Import
still performs its existing decode/validation, reservation and execution creation
boundaries. All six CreateGame boundaries across preparation/import remain.
The unchanged encode result and slower small-stage samples illustrate measurement
noise/tiering: no serialization improvement is claimed from this Engine phase.

Small Quick Play (three warmups, 30 samples) is essentially unchanged: Prepare
3.66 -> 3.76 ms, import 17.61 -> 19.22 ms; import p95 22.52 -> 22.95 ms. There is
no claimed small-world Quick Play gain. Its import allocations fell only from
5.33 to 5.28 MB.

Quiet Rogue Round Start, original probe, 30 samples each:

| Distant Dormant monsters | Before p50 / p95 ms | After p50 / p95 ms | Before -> after allocations |
| --- | ---: | ---: | ---: |
| 0 | 1.127 / 3.457 | 1.041 / 3.125 | 0.752 -> 0.757 MB |
| 100 | 13.254 / 42.125 | 4.250 / 6.065 | 7.088 -> 1.910 MB |
| 442 | 801.806 / 1,010.589 | 18.986 / 120.502 | 105.990 -> 5.476 MB |

The representative 442-unit Alert Round Start fell from 817.26 to 36.08 ms median
(after p95 43.20 ms), with 114.18 -> 13.45 MB allocated. Dormant/Alert rules did
not change. These improvements come from generic placement and attack query work;
Dormant still does not remove Units from physical occupancy, validation or state.
The quiet-round tail includes first-use/tiering outliers; warmed performance should
be checked separately when setting a production budget.

## Browser verification

Used the repository Playwright skill and separately reviewed temporary Edge script
against isolated localhost:5081, with fresh contexts, external requests blocked and
service workers disabled. Only in-memory served JavaScript was instrumented. No
production browser code was changed. Browser and temporary host were closed;
5080 was preserved. Browser results are individual traced runs, not p50/p95.

| Browser flow | Before total | After total | After final update received |
| --- | ---: | ---: | ---: |
| 100-distant Beastmaster Move | 28.99 s | 0.919 s | 0.301 s |
| 100-distant Summon Raptor | 30.55 s | 1.745 s | 0.270 s |
| Saved 442 Quick Play | 43.95 s | 15.197 s | 14.963 s |

Final-update time includes request submission, server work, transport and client
receipt; it is not pure Engine CPU. The remaining Move/Summon interval includes
intentional animation. Summon rendering spent about 70 ms in instrumented
renderFrame calls, not the whole 1.745 seconds. The Quick Play check submits the
saved generator definition through the preparation endpoint and the normal
`delvePlayDesign` import routine; it does not re-time procedural generation.

Verified all five Summon choices and successful submissions; Raptor disappears
from legal actions at its creator limit while the other four remain. A separate
nearby-enemy fixture verifies Attack and all five Summons together after Move.
That fixture uses an Automated enemy on Hold Position; its post-Summon total
also includes the ensuing enemy activation and animations.

FoW assertions exclude distant IDs from browser observations. Desktop 1440x1000
and mobile 390x844, Miniatures and Cardboard were exercised and screenshots
inspected. Large/small/large/small scene replacement kept GPU counters at 11
geometries and 14 textures after assets were warm; old summoned Figure nodes were
removed. Page teardown produced zero nodes, Units, geometries and textures.
All completed cases had zero page errors, console errors, failed requests or HTTP
errors. Initial script attempts needed corrections for a collapsed control and
fixture agency; these were QA script failures, not application changes.

## Correctness verification

- Independent exhaustive-reference discovery keeps the previous whole-board loop
  and ordered occupancy algorithm. It does not call the optimized placement
  predicate. 288 geometry cases cover both footprint combinations, corners/edges,
  occupied Cells, walls/doors/internal edges, terrain restrictions, Amphibious,
  Phase and machines. Additional cases cover fixed components, granted Equipment/
  Item sources, scenario-local Types, side/creator limits, Unique, restoration,
  exact ordered candidate serialization and re-enumeration after movement/creation.
- Placement tests cover overlapping large Figures, excluded IDs, defeated/removed
  Units, malformed references, fresh contexts after Unit/Type/board/terrain
  mutations and copied hypothetical states. Attack equivalence compares batched
  candidates with the original per-target EvaluateFrom path.
- Existing suites cover SummonerUnitId, deterministic replay/continuation,
  serialization, source accounting, FoW safety, Dormant/Alert, activation ordering,
  Orders, Automated decisions and creation/Designer round trips.
- Final focused equivalence/lookup run: **307 passed**. Full Engine suite: **3,061 passed**.
  Node browser/client and dispatcher suite: **1,016 passed** (using the documented
  installed Python override for model preparation).
- Full Release Web suite: **722 passed, 12 failed**. All 12 failures are in
  ScenarioContentReferenceTests and report conflicting `knightly-boots` provider
  identities. The identical tests fail against frozen original Engine, Generation
  and Web DLLs (SHA256-verified copies; 12 failed, 4 passed in that class).
  They predate Phase 1 and were not weakened or changed. Full solution build
  succeeds; the restored Web test assets retain an NU1900 vulnerability-feed
  availability warning. No new package or feed changes were made.
- Independent read-only review found no remaining significant correctness issue.
  It identified the malformed-state discovery case, which was fixed and tested.

## Remaining cost, risk and Phase 2

Measured after instrumentation: saved-world Web import took 16.77 seconds, of
which five JSON serializations took 16.18 seconds; nested effective-stat
AbilityModifier evaluations took 15.90 seconds. The three CreateGame calls took
0.46 seconds combined. These are inclusive timings, not additive categories.
This confirms the investigation's separate publication/serialization bottleneck.

Recommended next scope: avoid repeatedly deriving all effective stats during
serialization, reuse a correctly scoped derived-stat snapshot for one authoritative
state version, and then review redundant detachment/publication boundaries.
Preserve detached responses, request isolation, FoW projection and save/replay
contracts. Measure those changes before pursuing new persistence, event sourcing,
session ownership, client deltas or Three.js architecture. None is implemented here.

The main Phase 1 maintenance risk is future code retaining a PlacementLookup
across mutations. Keep it internal and short-lived; do not attach it to GameState.
Creation still allocates per insertion, attack enumeration still visits targets,
and derived stats still scale with Units/Abilities. This phase has not proved all
query scaling problems solved, but the measured Beastmaster problem no longer
requires a broad state redesign.

Practical performance regression targets on this machine/fixtures, Release with
three warmups and 30 samples: isolated 100-distant Summon candidates p95 < 5 ms
and < 0.5 MB; 100-distant Move/Summon p95 < 50 ms and < 3 MB; 442-distant
Move/Summon p95 < 100 ms and < 6 MB. Keep the structural 45 Summon placement
checks (five 1x1 Abilities) independent of board area. Quiet 442 Round Start
should remain < 250 ms p95 and < 10 MB. Saved-world preparation should stay
below 2 seconds on this fixture; import remains a Phase 2 target. Treat these as
benchmark budgets with retained distributions, not timing-sensitive unit tests.

Changed implementation files: `AbilityExecution.cs`, `EdgeReplacementRules.cs`,
`GameEngine.cs`, `SpatialRules.cs`, `SummoningRules.cs`, new `PlacementLookup.cs`.
New tests: `PlacementLookupTests.cs`, `SummonCandidateEquivalenceTests.cs`.
New tooling/documentation: `tools/PlacementQueryBenchmark/` and this report.
Pre-existing workspace edits were preserved; no rules, authored scenarios, content,
Web production code, client code or CONSTRAINTS.md edits belong to this phase.
