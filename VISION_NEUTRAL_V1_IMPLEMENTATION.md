# Vision & Neutral Interactables V1 — implementation and verification

Implemented and verified on 2026-11-11. Rules are in [CONSTRAINTS.md](CONSTRAINTS.md);
the implementation guide is [Vision and Machines](VISION_AND_MACHINES.md).

## Delivered behavior

- `SightDistance` is an effective stat: base 5, authored Unit Type override,
  existing self/adjacent/Equipment/activation modifiers, minimum zero. Passive,
  swept and Automated vision use it. A query captures its source range rather
  than recomputing it for each ray; ordinary observations preserve authoritative
  values and cache signatures include the effective range.
- `RevealDistance(X)` uses shared FoW geometry, the actor's footprint and effective
  Phase, ordinary source accounting and permanent Side discovery. A standalone
  no-effect use is legal. Machine Reveal uses its own origin, ignores only its own
  Machine identity, and requires new discovery. Operator Phase does not transfer.
- Phase and Amphibious, including Equipment grants, persist while Lying. Shared
  capability queries govern placement, initial inventory, movement, hypothetical
  endpoints, relocation/Swap and restoration. Lying still prohibits ordinary
  actions. Existing removal paths were checked: Equipment drops on actual defeat,
  together with removal of the figure; no living bearer capability-loss rule was
  added or required.
- Binoculars grants SightDistance +3; Swim Tube grants Amphibious. Both catalog
  idea entries are now Created. Operate is explicitly declared on Barbarian,
  Rogue, Cleric, Wizard, Beastmaster, Jackalope and Giant Toad.
- Machines support optional total and per-Side limits. Immutable instance counts
  are copied and serialized separately from Ability/Equipment charges. Resolution
  revalidates legality and commits discovery, inputs and successful use together.
  Existing Resource output retains its output-Cell restriction and accounting.
- Beacon is a neutral one-Cell Machine on Stone Floor, blocking movement, attack
  LOS and ordinary vision. It reveals distance 15, once per Side per instance,
  without storage, production, input costs or automatic lantern discovery.
- The two companions share a decision-local preference over authoritative choices.
  It uses known Machines and conservative geometry, considers only immediate or
  one-Move opportunities, preserves Orders and budgets, rejects known hazardous
  arrivals, and regenerates normal choices after movement. Deterministic ties use
  movement cost, destination y/x, Machine identity and candidate key. Existing
  ExploreFoW / Follow Summoner / Follow Friendly fallback remains.
- Civilization uses a dedicated seed stream, 1–4 targets scaled by map area,
  and at most 96 placement attempts per target. An open protected 5×5 pad supplies
  the local connectivity proof for ordinary and 2×2 movement. Placement commits
  Stone Floor and Machine atomically. Routes, topology and Population respect
  Machine blockers; diagnostics and preview frames include Beacons. There is no
  new Site, Building or Designer density control.
- Three.js uses shared stone pieces, an emissive lantern and two transparent
  rotating beams. One shared frame loop animates visible onscreen Beacons, without
  SpotLights or timers. Hidden/offscreen/cutaway Beacons do not sustain animation.
  Owned resources and rotor references are disposed with scene records. Enhanced
  2D, flat Three, Tabletop Miniatures and Cardboard use existing rendering paths.

Current GameState copies, restoration, event-time observations, replay and
WLD1/DELVE1 round-trips are tested. No migration or historical-seed compatibility
layer was introduced. Current recipes and content remain deterministic; obsolete
development formats and historical layouts are not a supported contract.

## Files and boundaries

| Area | Changed implementation files |
| --- | --- |
| Stats, capabilities, content | Engine `Model`, `ObservationStatContext`, `Abilities`, `ContentDefinitions`, `ContentDescriptions`, `UnitAuthoring`, `UnitAuthoringSchema`, `MechanicCatalog.Abilities`, `MechanicCatalog.Specialized`, `UnitRoster`, `EquipmentRoster` |
| Vision and actions | Engine `FogOfWar`, `AttackRules`, `AbilityExecution`, `AbilityCostRules`, `GameEngine` |
| Occupancy | Engine `SpatialRules`, `MovementRules`, `ExternalMovementRules`, `ScenarioCreation` |
| Machines and automation | Engine `Machines`, new `BeaconBehavior`, `GameplayQueries`, `AutonomousQueries`, `DefaultAutomatedProvider` |
| Generation | Generation `BoardPlanning`, new `BeaconGenerator`, `CivilizationGenerator`, `TerrainAnalysis`, `WorldGenerationPipeline` |
| Web | `BrowserProjection`, `ContentBrowserCatalog`; `wwwroot/board-visuals.js`, `three-tabletop.js`, `three-viewport.js`, `world-generator.js` |
| Verification | New Engine `VisionCapabilityTests`, `BeaconTests`, `BeaconBehaviorTests`, `BeaconGenerationTests`; updated nearby content, capability and generation assertions; Web `MachineProjectionTests`, `ScenarioContentReferenceTests`, `three-viewport.test.cjs` |
| Tools | `tools/EngineResolutionBenchmark/Program.cs`, new `VisionBenchmark.cs`, benchmark README |
| Documentation | `README.md`, `CONSTRAINTS.md`, `ABILITY_CONTENT.md`, `MECHANIC_CATALOG.md`, `EQUIPMENT_CONTENT.md`, `EQUIPMENT_CATALOG_IDEAS.md`, `WORLD_GENERATOR_DESIGNER.md`, `THREE_VIEWPORT.md`, `VISION_AND_MACHINES.md`, this report |

Content-reference Web tests now use their own empty canonical publication directory,
instead of depending on mutable local published content. No authored scenarios or
local publications were modified to make tests pass.

## Verification results

- Solution build succeeded. Cached Web test dependencies emitted NU1900 because
  the package vulnerability feed was unreachable; no compilation errors.
- Full Engine suite: **3,204 passed**. Full Web suite after the final projection
  correction: **752 passed**.
- Full Node and dispatcher suite, with the optional animation benchmark enabled:
  **1,027 passed, zero skipped**. The documented installed Python/Pillow override
  was needed for existing model-preparation tests.
- Focused generation/planning suite: 60 passed. Focused Machine projection suite:
  5 passed. Independent read-only review found one immediate-operation tie-break
  issue; corrected and covered with multi-Machine/source/candidate-order tests.
- Coverage includes modifier and posture changes, cache invalidation, footprint
  geometry, Phase, swept discovery, no-effect Reveal, Machine self-exclusion and
  other blockers, Side isolation, current transport/restore/replay, use-limit
  combinations and stale choices, hidden geometry parity, Orders and fallback,
  bounded generation/connectivity and renderer cleanup.

## Performance

Windows, .NET 10.0.12, Release, workstation GC. Vision measurements use three
warmups and seven samples, synchronous thread allocations. Values below are
medians; samples are diagnostic, not performance guarantees. The 100-Unit fixture
is 120×120 with deterministic rocks and legal source positions. Changing-refresh
measurements move one source, forcing cache refresh for the source set.

| Workload | Median ms | Allocated bytes |
| --- | ---: | ---: |
| Cached FoW, range 5 / 8 / 15 | 0.70 / 0.77 / 1.34 | 156,888 each |
| Changing FoW, range 5 | 10.27 | 6,984,592 |
| Changing FoW, range 8 | 15.16 | 9,507,320 |
| Changing FoW, range 15 | 44.89 | 26,361,704 |
| Reveal area 15 | 0.46 | 289,520 |
| Beacon move decision including Engine | 3.85 | 2,449,296 |
| Civilization, 120×120, four Beacons | 305.98 | 67,234,440 |
| Existing 30×30 obstacle resolution control | 32.67 | 21,717,296 |

Existing performance workloads were also rerun sequentially. Phase 1's 442-distant
Summon candidate query: 0.65 ms / 345,800 bytes; complete Summon resolution:
32.27 ms / 6,927,904 bytes. Phase 2's 442-distant streaming import:
104.88 ms / 89,745,776 bytes; browser projection: 36.79 ms / 1,448,648 bytes;
JSON emission: 3.90 ms / 314,872 bytes. Five samples, three warmups.

The retained Phase 3 80×80 snapshot control measured Patrol 70.45 ms /
39,168,080 bytes and Normal 216.14 ms / 85,855,264 bytes (three samples,
one warmup). Both uninterrupted and serialize-at-every-decision traces match
within the current implementation. Published Phase 3 medians were approximately
69/208 ms and 32.5/75.5 MB: the principal latency improvements remain, while these
controls show additional allocation overhead. Historical trace digests are not
compared across changed schemas. No compatibility adapter was needed or added.

Four visible Beacons plus a Barbarian miniature in Edge: render p50/p95
**2.8/3.7 ms**, frame interval p50/p95 **16.7/16.8 ms**, 79 draw calls and 25
geometries, with no pending/missing textures. This is a short 36-frame UI sample.

The opt-in Node scene benchmark separately measures warmed CPU orchestration
with a stub renderer. At four Beacons, Tabletop median/p95 was 0.28/0.61 ms;
V8 sampling estimated 193,057 allocated bytes/frame. One Beacon was 0.28/0.56 ms
and 182,876 bytes/frame. These figures include existing viewport and harness work,
not GPU time or Beacon-only allocations. All 6/24 owned resources were disposed
and zero frames remained scheduled after removal. Forced-GC retained heap includes
shared caches, so it is not used as a claim of zero retained JavaScript memory.

Raw local evidence: `.playwright-cli/vision-performance.jsonl`,
`vision-obstacles-performance.txt`, `vision-phase1.jsonl`, `vision-phase2.jsonl`,
`vision-phase3.jsonl`, `vision-node-final.log`.

## Edge QA

Used the [playwright-cli skill](.agents/skills/playwright-cli/SKILL.md) through the
protected dispatcher documented in [BROWSER_INSPECTION.md](BROWSER_INSPECTION.md),
an isolated 5081 host and test-owned DELVE1 scenarios. The existing 5080 host and
local content were preserved; the isolated host was stopped afterward.

Verified both Equipment entries; a Hero starting on Water; Hero Operate,
discovery and exhausted blue-Side use; Jackalope and Giant Toad Move + Operate;
hidden Beacon omission; generated Beacon preview/counts/export; Enhanced 2D and
Tabletop Miniatures/Cardboard; desktop and narrow layouts; repeated scene/mode
replacement; and the corrected Reveal journal text. Successful final inspection
reports contain no console or request errors. Independent Side counts are also
covered in Engine tests; resource cleanup/offscreen scheduling is asserted in Node.

Evidence directories under `.playwright-cli/browser-qa/`: `inspection-8N8Ac0`
(Equipment), `inspection-xjOl7f` (Hero/discovery/2D/narrow), `inspection-702NdR`
(Jackalope), `inspection-vl1rdU` (generation), `inspection-ptkgF3`
(four Beacons, Miniatures/Cardboard, metrics), `inspection-1weZ1Q`
(final Giant Toad journal and replacement).

## Remaining limits

No unresolved gameplay-rule conflict remains. No Healing, Recharge, theft,
capability-loss consequences, Hero inheritance or persistent AI planner was added.
Beacon placement may deliberately omit targets when a safe pad cannot be found.
ExploreFoW forecasts use decision-time range; actual swept discovery reevaluates
auras at transit positions. Large ranges still cost more geometry work, and the
100-Unit range-15 refresh allocates about 26 MB. Browser measurements are short
local samples, not long-duration GPU or memory-soak guarantees.
