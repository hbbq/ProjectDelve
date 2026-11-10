# Performance Phase 2 — state detachment and publication

## Result and scope

Phase 2 removes derived-stat and presentation evaluation from Web ownership
copies. It preserves the existing in-memory authoritative game, checkpoint and
event journal architecture. Phase 1 bounded Summon enumeration and placement
indexes remain intact. No gameplay, authored scenario, client protocol,
persistence or CONSTRAINTS.md changes are included.

On the saved 442-monster world, legacy import service fell from a median
14,760 ms to 230 ms. Streaming admission-to-baseline fell from 10,023 ms to
229 ms. These measure different completion boundaries: the old streaming path
continued building an unused legacy aggregate after publishing its baseline.
They exclude browser projection, network transfer and rendering.

Warm browser Quick Play is 780 ms p50 / 898 ms p95. A fresh-host first run is
3,155 ms: the two-second budget is met warm, while cold startup remains above it.
With 442 distant monsters, combined command service, projection and JSON emission
are below 100 ms p95 for both post-Move and Raptor in a separate pipeline control.

The improvement comes primarily from removing work, rather than making the JSON
serializer faster. Full state remains authoritative in memory between commands.

## Measurement and reproduction

Use [WebPublicationBenchmark](tools/WebPublicationBenchmark/README.md) in Release.
It can compile against frozen Engine/Web assemblies and records raw elapsed,
allocation and GC samples, module IDs, stage-first samples, publication counts,
payload sizes and nearest-rank percentiles. Captured results are in
[phase2-results.json](tools/WebPublicationBenchmark/phase2-results.json).

The baseline is commit `360e54a`, with its assemblies frozen before production
edits. Measurements use .NET 10.0.12, workstation GC, 16 logical processors and
the same Windows machine. Heavy benchmarks, builds and browser measurements ran
sequentially. Allocation figures are process-wide allocated bytes, not retained
heap size. MB means decimal MB. Temporary nested profiling measures thread-local
allocations; its inclusive times overlap and must not be added together.

Fixtures:

- Saved `assets/previews/population-v2-442-study.delve`: SHA256
  `22e99a8af8f87f3e06e6267bd1851fd54f484e641ee46c5960e956b52f50de70`.
  It has 442 monsters, 4,244 authored terrain Cells and 136 Edges. Quick Play adds
  five Heroes, making 447 Units. This content is measured, not asserted as an
  immutable gameplay test fixture.
- Dedicated small fixture: 12×12 board, Beastmaster and one Dormant Grunt.
- Dedicated large fixtures: 120×120, the same nearby configuration plus 100 or
  442 distant Dormant Grunts. The latter has 444 Units, 443 Dormant monsters.
- Quiet Round Start substitutes Rogue for Beastmaster to avoid conflating its
  action query with Dormant processing.

After measurements normally use three warmups and 30 samples. Small before
controls also use 30 samples; costly before action controls use five samples for
100 distant monsters and three for 442. The original saved-world import control
uses three stage-first samples. Its maximum is not a reliable p95. Preparation
and quiet-round controls are measured separately with more repetitions below.
No estimated speedups are presented as measured results.

The harness measures Engine command service, then projects and serializes the
actual collected SSE updates separately. It stops and joins the command runner
outside the timed interval before further samples, including the old discarded
aggregate work. The original three-sample saved-world projection control predates
that harness refinement; its allocation/timing can include concurrent leftover
aggregate work and is diagnostic only. Browser timing is reported independently.
The action controls choose Stay to complete Move and query the post-Move Action
choices. Browser QA performs actual movement; these controls do not measure long
movement paths or large Alert cascades.

## Ownership and snapshot invariants

`GameState.DetachedCopy()` uses the Engine's raw copy path. It never reads the
effective-stat dictionaries or builds cards. `Copy()` now also clones pending
candidate/path collections, WorldEffects composition and the private frozen
observation values. Existing immutable content records/collections and immutable
vision analysis can be shared; mutable state containers cannot.

`PublicationSnapshot` is a Web-local copier. Its identity map lasts for one copy
operation. When `Result.State` and `StateAfter` are the same reference it copies
that state once. Different references are never merged, including two event
states with the same revision. Events, nested movement paths, decisions,
scenario metadata and controller containers are independently owned. Already
computed presentation DTOs can be copied through JSON; copying a raw response
never evaluates its lazy `Presentation` getter.

The single command runner still owns executable state. Stable checkpoints and
journal entries are private detached graphs, immutable by ownership. An owned
checkpoint can be retained directly by the journal. Each live or reconnecting
subscriber receives its own detached graph; public snapshots and fault recovery
also copy. A subscriber cannot mutate another subscriber, the checkpoint, the
journal or execution state. No public object is used as a reusable validation or
cache token.

Fault rollback restores the last stable checkpoint. Previously emitted event
snapshots retain their original explored state. Observation is performed against
each event-time authoritative state, so later discovery cannot rewrite an earlier
event or reveal hidden Units through a replay.

## Effective-stat boundary

Raw authoritative serialization retains its existing fields and semantics.
Effective values have not been removed from public JSON. Generic Web detachment
simply no longer serializes the authoritative state to copy it.

`ObservationStatContext` exists for one `FogOfWar.Observe` call. It indexes actual
living, upright, placed passive providers, including native and equipment-granted
Abilities, and memoizes each recipient's modifier totals. Provider, Ability and
modifier order, relationship/adjacency checks, per-mechanic checked summation,
stacking and the existing effective-stat formulas are retained. Ordinary gameplay
queries continue to use their existing authoritative calculation.

The four stats are calculated from the authoritative source before redaction,
including effects from hidden aura providers. Only the recipient values are
retained on the observation. Observations without FoW also freeze these values.
Copies preserve the frozen values so subsequent serialization does not derive
them again from a sanitized state. The provider index and modifier memo never
become serialized gameplay state or survive mutation/state replacement. An
observation remains presentation data; it is not an executable continuation.

`BrowserResponse` now observes once and passes that same result to card creation
and sanitization. Reference-based reuse is scoped to this one response. It does
not collapse intermediate states or share observations between viewing Sides.
Authoritative geometry remains available for legal previews of summoned Types
that are absent from the observation.

## Import and publication flow

`ScenarioDefinitionJson.Materialize` and the transport counterpart return both
the parsed definition and its validated, newly created state. Existing `FromJson`
and `Decode` contracts delegate to them. Parsing, size/version checks,
pre-creation limits and Engine validation remain in place.

The Quick Play handler reuses that exact privately owned materialization to find
placements. It copies its input and validates the completed, changed scenario.
Preparation therefore creates twice instead of three times. Import admission
uses the state already created by transport decoding for controller validation,
instead of creating the identical preview again. Execution still creates and
validates against the then-current canonical content. Import creates twice
instead of three times; the full path drops from six creations to four.

The materialization result contains ordinary mutable owned objects, not proof
that later mutations are valid. It is never retained as a cross-request cache.
Tests change or remove canonical content between admission and execution to
verify that final validation has not been bypassed.

Streaming commands now avoid collecting and detaching the unused legacy aggregate
response. Legacy callers still receive their aggregate; Gambit still gets the
history it consumes. Every event callback, boundary and commentary publication
retains its order and metadata. Acknowledgements, duplicate/stale command checks,
session replacement, sequence/cursor semantics, bounded journal and slow-subscriber
behavior retain the existing protocol. No state delta or durable replay protocol
has been introduced.

## Measured service and allocation results

Times are milliseconds. “Before” is p50; “After” is p50 / p95. Allocation is the
median for the named service interval. Projection/JSON rows are separate work,
not included in command service.

| Operation | Before | After | Allocated before → after |
| --- | ---: | ---: | ---: |
| Saved442 legacy import | 14,760.17 | 230.33 / 252.52 | 749.97 → 106.50 MB |
| Saved442 streaming import to baseline | 10,022.67 | 228.69 / 232.93 | 543.65 → 106.50 MB |
| Small post-Move (Stay) service | 17.87 | 1.11 / 2.17 | 3.91 → 0.58 MB |
| Small Raptor service | 14.91 | 1.63 / 2.95 | 4.76 → 0.83 MB |
| 100-distant post-Move service | 248.56 | 3.40 / 9.88 | 48.31 → 1.46 MB |
| 100-distant Raptor service | 138.55 | 3.46 / 9.32 | 78.56 → 1.87 MB |
| 442-distant post-Move service | 1,630.91 | 27.57 / 144.20 | 441.30 → 4.34 MB |
| 442-distant Raptor service | 3,417.36 | 21.11 / 23.32 | 954.47 → 4.52 MB |
| 442-distant raw state detachment | 310.41 | 0.005 / 0.015 | 84.005 → 0.013 MB |

After, the full 442-distant post-Move update batch takes a separate 24.66 / 31.00
ms to project and 3.42 / 4.73 ms to serialize; Raptor takes 42.29 / 49.58 ms and
6.29 / 6.82 ms respectively. These separate marginal percentiles are not a
measured end-to-end percentile. Both commands remain comfortably within the
500 ms service-plus-publication budget on these fixtures.

A separate sequential pipeline control measures resolution, collection of every
publication, browser projection and JSON emission inside one timer. With three
warmups and 30 samples, 442-distant post-Move is **70.31 / 94.38 ms**, allocating
8.92 MB median; Raptor is **65.44 / 79.65 ms**, allocating 11.48 MB. This confirms
the combined in-process budget. It is a conservative sequential pipeline, not
the actual concurrently streamed HTTP path, and excludes network and animation.

Projection is not universally cheaper in isolation: the small post-Move batch
increases from 0.30 ms to 1.38 ms, and its allocation from 0.21 MB to 0.98 MB.
Work previously done implicitly during detachment now occurs at its correct
observation boundary; independent subscriber ownership also requires copies.
The combined service reduction is substantially larger.

Saved442 browser projection after import costs 78.91 / 85.81 ms and 2.40 MB;
serialization costs 1.16 / 1.31 ms and 0.323 MB. Its baseline SSE JSON is 308,948
bytes on both versions. The 442-distant Raptor batch still sends six updates and
about 1.76 MB. This phase avoids duplicate computation and in-memory copies; it
does not remove existing duplicate state fields from the wire contract.

The full saved-world browser response is structurally identical before/after
after normalizing the random session ID. This includes effective values,
presentation, hidden-state filtering, field names and scenario metadata.

### Attribution

Before, one instrumented saved-world legacy import took 16,159.75 ms. Five
detachment serializations consumed 15,140.09 ms; 5,420 nested `AbilityModifier`
calls consumed 14,847.35 ms and allocated 505.23 MB. Deserialization was only
80.16 ms. These nested scopes explain the root cause; they are not additive.

After, one instrumented warmed legacy import has three detachment calls totaling
0.49 ms. Small DTO JSON serialization/deserialization totals 0.057 / 0.048 ms;
two raw state copies take 0.275 ms and 0.264 MB. There are no `AbilityModifier`
calls, observation or card calculations inside import detachment. Streaming
import publishes one baseline; the checkpoint and subscriber each receive the
required ownership boundary without an unused aggregate response.
The earlier profile includes an initial `Snapshot()` call that the new harness
excludes from its service timer; the detachment invocation counts therefore have
that small scope difference. Publication reuses its privately owned checkpoint;
it does not remove the copy required for an independent observer.

In a separately instrumented browser projection, one observation and one card
projection replace repeated observation. One stat context build and 40 modifier
requests calculate the four stats for ten observable Units. Its 80.78 ms modifier
scope shows that real passive-provider/adjacency work still exists at the proper
boundary. The uninstrumented projection timing above is the latency comparison.

Two `CreateGame` calls now dominate import: the instrumented stream sample spends
305.64 ms there, including 898 placement-index builds (118.96 ms, 78.83 MB),
repeated placement and FoW initialization. This is remaining construction work,
not a reason to bypass final validation.

### Preparation, creation, GC and Dormant controls

Separate preparation runs use three warmups and 30 samples on both versions.
The final `preparation442-handler` runs pass the captured canonical content and
authoring-limit callback exactly like the handler. Earlier preparation samples
recreated default canonical content; they are retained as diagnostics, not used
in this table. The initial three-sample import run's preparation median of
1,243 ms also contains substantial startup/tiering work.

| Saved442 stage | Before p50 / p95 | After p50 / p95 | Median allocation before → after |
| --- | ---: | ---: | ---: |
| Quick Play parse, validation and preparation | 297.94 / 377.74 ms | 243.17 / 368.10 ms | 149.66 → 106.95 MB |
| Transport encoding | 4.54 / 6.37 ms | 4.11 / 5.75 ms | 3.318 → 3.318 MB |
| One executable `CreateGame` | 107.21 / 123.63 ms | 104.12 / 119.72 ms | 47.401 → 47.274 MB |

Streaming import admission falls from 228.57 ms median to 118.36 ms; the rest
of its 228.69 ms total is execution/publication/reader scheduling. These are
actual admission timestamps, not subtraction of independently measured medians.
Legacy import median GC counts per operation change from 87/7/1 to 13/3/1 for
generations 0/1/2. Raw GC samples and the streaming counts are retained in the
results file; collection counts are sensitive to heap history.

The three-warmup quiet-round run after Phase 2 has p50/p95 21.41/115.62 ms;
the first two measured samples are 115.62 and 122.90 ms. A separate 20-warmup,
100-sample control gives before 14.76/23.32 ms and after 17.23/23.08 ms, with
median allocations 5.28 → 5.08 MB. There is no claimed Dormant algorithm speedup
in this phase. The warm 100 ms budget is met; startup tails are still visible.

### Edge end-to-end checks

The browser probe uses a fresh temporary Edge context against the isolated
Release host on port 5081. In-memory response hooks record SSE receipt, main-thread
render work, long tasks, scene resources and errors. The shipped JavaScript is
unchanged. End-to-end means Quick Play preparation through the actual import
command, SSE receipt, reconciliation and controls ready. It excludes initial
navigation to the application and does not claim completion of every asynchronous
asset load or a GPU timer measurement.

| Saved442 browser run | Result |
| --- | ---: |
| Phase 1 fresh-host control, one run | 18,849.9 ms |
| Phase 2 fresh host/context, one run | 3,154.8 ms |
| Phase 2 warm server, fresh context, one run | 1,080.8 ms |
| Phase 2 repeated Quick Play, three warmups + 30 samples | 780.2 ms p50 / 897.5 ms p95 |

The two-second budget is met warm, **not demonstrated cold**. In the fresh-host
run Quick Play HTTP takes 995 ms and command admission HTTP takes 644 ms; the
baseline SSE arrives 3,076 ms after starting Quick Play. The remaining interval
contains execution, observation/publication and scheduling, without exact cold
sub-stage attribution. Maximum observed renderState/renderFrame calls for that
transition are 34/34 ms (nested scopes, not additive). A single cold sample is
not a cold p95, and runtime/JIT startup is a hypothesis for part of the warm/cold
difference rather than an exclusive measured attribution.

On the 100-distant Miniatures fixture, actual Move-to-idle is 905 ms and Raptor
to-idle is 1,696 ms. Last SSE arrives after 179/149 ms respectively. The remaining
interval includes intentional event playback, UI interaction and rendering;
it must not be called Engine or server latency. The nearby-enemy fixture verifies
Attack and all five Summons together, then Raptor and a normal human Defence-roll
continuation. Its additional automated attack playback takes longer by design.

QA passes Raptor, Jackalope, Giant Toad, Dire Bear and Light Elemental, creator
provenance, Raptor's next-round limit, hidden distant Units, cards and effective
stat fields, desktop/mobile, Miniatures/Cardboard, event playback and reconnect
baseline equivalence. Repeated scene replacement removes old nodes; pagehide
disposal ends with zero nodes, unit roots, geometries and textures. Captured
desktop, miniature and mobile screenshots were visually inspected.

There are no page/console errors or HTTP failures. One SSE request is deliberately
aborted by the reconnect check; there are no other failed requests in successful
runs. An initial attack QA attempt timed out because the harness did not handle
a normal Defence-roll prompt when the enemy activated first. The harness was
corrected and the scenario passed; production gameplay was unchanged. Browser
reconnect checks a fresh baseline; cursor replay/gaps and slow subscribers are
covered by the Web tests, not claimed as browser cursor-replay coverage.

## Regression coverage and remaining work

New Engine tests cover raw copy isolation (including candidates, paths, decks and
composition), independent states, frozen observations, native/equipment modifiers,
stacking and precedence, Fury/world/turn modifiers, movement, lying/defeated or
removed providers, walls, multi-cell adjacency, neutral relationships, hidden aura
providers and serialization. A separate oracle uses unchanged gameplay stat
queries. An overflow test preserves existing failure semantics even for a
manually constructed invalid modifier list.

New Web tests cover event-time states with the same revision, independent live
subscribers and reconnect replays, mutable nested fields, checkpoint recovery,
fault after exploration, hidden providers, no-FoW legacy presentation and import
materialization. The commentary replay assertion now checks identical serialized
content and distinct mutable state, instead of relying on shared object identity.
Existing Phase 1 equivalence and Engine/Web suites remain part of verification.

Final verification:

- Release Engine: **3,078 passed**, including all Phase 1 equivalence tests.
- Release Web: **738 passed, 12 failed**. The same 12
  `ScenarioContentReferenceTests` fail on the frozen Phase 1 baseline because of
  conflicting `knightly-boots` provider identities. They are not weakened or fixed
  by this performance change. The ownership/materialization/commentary focused
  run passes all 27 tests.
- Node client and browser-dispatcher tests: **1,016 passed**.
- Release solution and standalone benchmark builds succeed. Cached Web test
  assets report NU1900 because vulnerability-feed access is unavailable; no feeds,
  dependencies or audit settings were changed.
- Independent static review passes found no remaining actionable defect.
  The isolated 5081 host and temporary Edge contexts were closed; the existing
  5080 host was preserved.

Warm regression acceptance on this machine: Quick Play browser p95 under two
seconds with three warmups and 30 samples; 442-distant command service plus
projection/serialization below 500 ms; quiet-round p95 below 100 ms after explicit
runtime warmup. Keep cold results separately, retain allocation and publication
counts, and require byte/structural contract equivalence and the ownership/FoW
tests alongside latency budgets. These are fixture budgets, not universal hosting
or roster guarantees.

## Recommended next phase

The measurements justify keeping this architecture. No new session layer,
persistence system or event sourcing is needed to solve the measured import and
Beastmaster stalls.

1. Attribute cold-start service separately, then consider a dedicated
   scenario-construction placement pass: build identity and
   spatial data incrementally within the private creation operation, preserving
   all placement and final validation. Rebuilding indexes for each inserted Unit
   remains a large share of the roughly 106 MB import allocation. Measure before
   changing it; do not cache indexes on publicly mutable GameState.
2. Profile the remaining passive-provider adjacency and FoW observation work on
   realistic mixed rosters. The current context already avoids repeated stats;
   further bounded spatial reuse must respect Walls, posture, source identity and
   hidden providers.
3. Only if network/remote-browser traces justify it, consider a separately designed
   static-board or state-delta protocol. Large full-state SSE batches remain, but
   JSON emission itself is now inexpensive. That work requires explicit reconnect,
   FoW, compatibility and recovery tests and is outside this phase.

Preserve the benchmark fixtures and ownership tests when adding mutable fields to
GameState or publication DTOs. The explicit copier is easy to audit, but new
mutable fields must be added deliberately. Retaining multiple private snapshots
still has a memory cost; this investigation measures allocations, not a long-lived
session retained-heap bound. Animation and Three.js work remain separate from
server processing and need their own evidence before refactoring.

## Changed files

- Engine: `Model.cs`, `FogOfWar.cs`, new `ObservationStatContext.cs`,
  `ScenarioDefinitionJson.cs`, `ScenarioDefinitionTransport.cs`.
- Web: new `PublicationSnapshot.cs`, `PlaytestGame.cs`, `PlaytestHost.cs`,
  `BrowserProjection.cs`, `GambitController.cs`, `WorldQuickPlay.cs`,
  `WorldGenerationApi.cs`.
- Tests: new `StateDetachmentTests.cs`, `EffectiveStatProjectionTests.cs`,
  `PublicationOwnershipTests.cs`, `ScenarioMaterializationTests.cs`; updated
  `DecisionCommentaryTests.cs`.
- Evidence/tooling: this report, a link from `PERFORMANCE_PHASE1.md`, and
  `tools/WebPublicationBenchmark/`.

Temporary instrumentation and browser hooks live only under `.tmp/perf-probe`;
they are not production code or a runtime dependency.
