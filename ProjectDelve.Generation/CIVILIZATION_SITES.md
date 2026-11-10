# Civilization and Sites — Milestone 1

World generation can now construct Campsites and small Settlements after natural
enrichment. A Settlement composes independent Building and Room Layout generators.
All generated structures use existing terrain, assemblies and shared Edges. Civilization
itself adds no inhabitants or scenario Objectives and introduces no movement rules.
Natural Terrain sampling and DNG1/DNG2 are unchanged.

The subsequent [Connections milestone](ROUTE_NETWORKS.md) adds route-aware placement,
exact published access, shared planning ownership and external Site connections.

[Population V2](POPULATION_V2.md) now consumes authoring-only Site Population Intent
after final geometry analysis. Camps publish sentry, resting, central and supply
areas, occupation bounds, an optional open patrol path and Connected/Separated Alert
intent. Clearing-centered and elongated roadside families use existing Campfire and
Table obstacles. Population may leave a Site vacant or use a compatible different
occupation; natural encounters do not require Sites. Settlements/buildings retain
their existing geometry. Construction reservations never become runtime ownership
or population occupancy. Population validates final placements and protects required
access, with bounded recorded fallback rather than modifying the Site at runtime.

## Execution and contracts

The explicit call graph is:

```text
WorldGenerationPipeline
  CivilizationGenerator
    CampsiteGenerator
    SettlementGenerator
      BuildingGenerator
        RoomLayoutGenerator
```

`WorldGenerationPlan` validates ordered Step IDs, parameters and dependency
ancestry. Civilization runs at most once, after original analysis and enrichment,
before final analysis. Enrichment cannot run after construction because its
terrain-only routing does not understand constructed Edges. Read-only analysis can
still repeat. This is concrete orchestration, with no registry, recursive recipe
interpreter or external DSL.

Each specialized generator exposes `Plan(typedRequest, GenerationScope)` and
returns `Planned<T>` or `Rejected<T>` with a structured `PlacementFailure`.
Requests separate hard requirements from placement preferences. Failures identify
content, geometry, entrance, reservation, preparation, connectivity or budget
problems. Invalid materials and impossible dimensional constraints are rejected
before spatial retries. Stable request keys are validated programming contracts.

`GenerationScope` captures the root seed, stable nested path, effective content,
allowed private draft and one shared `PlanningBudget`. The public `Board` view is
read-only; draft mutation is internal to planning. The budget only decreases:
discarding a draft does not refund attempts or work. Civilization allows 512
nested attempts and two million work units. Candidate generation and per-generator
retry counts are also bounded. Trace output is restricted to accepted plans;
failed top-level requests have a bounded summary, without private partial edits.

Building and Room Layout can be called without Settlement or Civilization. Supply
an Engine-valid `PlanningBoard`, a bounded root draft, seed and stable scope path;
call `Plan`, accept its patch into the supplied draft, and commit the completed
root draft. The dedicated generator tests show this contract with local content.
No live GameState storage changes or generative Designer commands are involved.

## Atomic planning and content

`PlanningBoard` validates and detaches its input scenario, then indexes terrain,
shared Edges and reservations. `EdgeKey.Between` normalizes cardinal shared Edge
identity. A `BoardDraft` has a private copy of sparse writes and reservations over
the immutable source terrain. Children see provisional parent changes. Conflicting
contributions require explicit replacement; existing Edges cannot silently become
Doors or disappear. Protected Cells and both endpoints of an Edge must be writable.

Each candidate is planned privately, including children, access checks and final
aggregate preparation limits. A child patch is bound to the exact parent and its
revision. A root patch is bound to the exact board and revision. Stale patches are
rejected. Root commit validates the complete proposed scenario through
`GameEngine.CreateGame` before swapping state. Failed commits preserve terrain,
Edges, content and reservations. Semantic objects and construction operations are
published only after root commit succeeds.

The board captures Canonical/Workspace definitions and composes scenario-local
content through existing content resolution. Draft content additions use typed
bundles, validate dependencies and reject identity conflicts. Assemblies reference
existing surfaces and Fixed Components. No generated content definitions are
needed for this milestone: Campfire and Table assemblies already exist. Web export
uses the existing Workspace capture boundary, so portable DELVE1 preserves the
effective definitions. Unsupported Workspace exports keep their existing errors.

Terrain changes and Edge-only commits invalidate current analysis. Reanalysis uses
the final effective terrain and constructed Edges; Closed Doors count as potential
ordinary passages. Original natural analysis remains immutable provenance.
Replaced enrichment opportunities and blocked enrichment entrances are filtered.

## Terrain preparation and generators

`PreparationPolicy` independently permits Tree clearing, Solid Rock excavation,
Water filling and authorized Edge replacement, with changed-Cell and weighted-cost
limits. Costs are preferences/diagnostics, not gameplay resources: Tree 2, Rock 4,
Water 6 and other changed terrain 1. Final Site totals count net source-to-final
changes, without charging every provisional repaint. Natural enrichment's
preservation caps do not constrain explicit construction policies.

* Campsite prepares an irregular small clearing with a cardinal access apron,
  places canonical Campfire and verifies connected surrounding ground. It considers
  analyzed and enriched clearing/open-area opportunities, then bounded candidates
  ranked by preparation cost. A minimum usable area, radius and optional Site role
  are hard requirements.
* Building chooses a bounded procedural rectangular footprint, prepares floor and
  an access apron, constructs exterior Walls and a usable Closed Door, and requests
  rooms. Plot/size, material, room count and exterior Door side or endpoint can be
  required; preferred position/orientation are soft. The complete interior is
  flood-checked from the actual exterior Door endpoint. Independent plots need an
  apron inside the board because outer-board Edges cannot carry Walls.
* Room Layout partitions a small prepared interior into one to three rooms with
  variable bounded splits. Building owns the shared partition/portal result.
  Adjacent rooms receive Doors on their shared partition. Optional existing Table
  furniture avoids portal endpoints and is accepted only if usable room space
  remains connected. Supplied exterior portals must match actual geometry and
  crossability. Existing interior Edges require explicit replacement permission;
  authorized replacement clears them privately before repartitioning.
* Settlement requests two to four Buildings in stable named lots around a shared
  street and small plaza. Entrances face the street, with short Gravel approaches.
  Candidate lots have varied dimensions; footprints, partitions, Doors and room
  counts vary by seed. Only actual footprints, aprons, access and plaza are
  prepared/reserved. Unused plot margins retain natural terrain. Final aggregate
  flood checks every usable room and exterior Cell together. A failed child or
  aggregate limit discards the entire candidate Settlement.

Water construction creates ordinary land terrain, including artificial islands.
Water-separated Sites truthfully require an external connection. No bridge,
layered terrain or automatic inter-Site connection is created. Dense natural
barriers may still isolate a valid Site; final scenario playability is later work.

## Semantics and deterministic identity

`SiteInfo`, `BuildingInfo` and `RoomInfo` expose stable object paths, exact compact
row-span geometry, usable areas, portals, preparation summaries and accessibility
facts. Sites retain original formation IDs and a source opportunity where present.
Buildings and Sites can bind an explicitly requested scenario role. Object IDs,
content Type IDs, tags and scenario roles are distinct. Roles do not create
Objectives, Sides, controllers or occupants.

Room interiors and usable spans distinguish furniture from usable floor. Portals
give real endpoint indices and canonical shared Edge identity. Connectivity facts
describe one-Cell ordinary potential traversal, including opening Doors. Counts of
available 2x2 placements are diagnostics; they do not promise movement for every
future Unit footprint. Future generators must query the current board and validate
their chosen occupants and destinations.

SHA-256 derives independent PCG streams from `CIV1`, root seed, stable nested
object path, purpose and deterministic attempt ordinal. Paths include Step ID and
the requested window identity. Site keys use requested ordinals, and Building keys
use logical lots, never accepted-object counts or display names. Selection,
placement, partitions, Doors and furnishing have separate streams. Adding a later
Camp does not reseed an earlier Settlement or Camp. Changed terrain/reservations
may legitimately alter placement. Regeneration starts at Natural Terrain, never
from the previously enriched or constructed board.

## Recipe and preview controls

The effective WLD1 suffix is `-C1-N2-V1-P2`: Civilization enabled, two Campsites,
one Settlement, medium preparation. `C` is 0/1, `N` is 0..8, `V` is 0..4 and `P`
is 1..3. Low/medium/high limits are respectively 320/650/1100 changed Cells and
1600/4500/9000 weighted cost per Site. Requested counts are bounded targets;
unplaced requests are reported. Presets supply editable defaults and completed
codes contain effective settings. Disabling construction retains the natural and
enrichment workflow; disabling both yields natural terrain with read-only analysis.
There is no WLD2 compatibility layer. DNG recipes and fingerprints remain intact.

Top-level preview retains eight frames when all stages are enabled. Accepted
`ConstructionOperation` batches record sparse terrain and Edge before/after edits,
parent identity and bounds. No full-board snapshot is retained per Building or
Room operation. The inspector replays from the first Civilization checkpoint,
including when entered from the final analysis frame. Selecting a parent inspects
its complete subtree. Room metadata selects the owning layout because shared
partitions/Doors belong to that layout. Before excludes the selected subtree;
After includes it. Terrain and Edge changes, removed Edges, semantic spaces,
entrances, costs and bounded placement failures can be inspected.

Accepted-plan overlays explicitly describe completed semantic spaces during partial
construction inspection. Connectivity overlays/diagnostics describe the accepted
server stage, not a new analysis of every provisional operation. The client only
replays edits and presents server geometry. Export and Designer always use the
final accepted result regardless of inspection selection. Existing zoom, pan,
new-seed regeneration and recipe copying are retained.

## Evaluation and performance

The reproducible gallery and measurements are under `assets/previews/`:

* `world-civilization-gallery.json`: completed codes and fingerprints.
* `world-civilization-site-details.png`: rooms, Doors, Campfires and approaches.
* `world-civilization-*.png`: before/after Grassland, dense Woodland, excavated
  Rock, artificial islands, mixed terrain and a 21-Cell-wide edge crop.
* `world-civilization-performance.json`: per-stage timings/allocations, planning
  work/attempts, terrain/Edge counts, Engine validation time/count, retained preview
  estimates, measured managed retention and UTF-8 payload sizes.

Visual review reduced the initial excessive rectangular plot preparation to actual
building aprons, a street/plaza and approaches. The final sheets show connected
variable rooms and Doors, natural margins, irregular Campsites and deliberate
Water-separated construction. All twenty profiled cases placed the requested two
Campsites and one Settlement. The corpus covers all five terrain presets at
50x50, 120x100 and 120x120, plus dense-family/mixed/edge cases at negative offsets.

Measurements use Release, three warmups and seven samples per case; report medians
from one local process. Raw preview estimates, managed retained preview heap and
payload bytes are different measurements. The retained-heap probe forces GC while
retaining only preview data, not the whole generation context or browser DOM.
Engine validation/materialization is a substantial share of construction cost.
There is no broad Engine storage optimization in this milestone.

Ranges across the five fixed-seed presets in the final isolated measurement run:

| Map | Total ms | Civilization ms | Engine construction validation ms | Total allocated MB | Retained preview MB | Response MB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 50x50 | 52–161 | 28–114 | 18–80 | 25–30 | 0.23–0.26 | 0.33–0.42 |
| 120x100 | 122–224 | 49–113 | 31–85 | 63–138 | 0.69–0.84 | 0.71–1.28 |
| 120x120 | 163–248 | 73–142 | 52–106 | 75–119 | 0.85–1.04 | 0.90–1.61 |

MB uses decimal bytes. Timings are observations, not acceptance thresholds; tiered
JIT and GC make early cases and repeated local runs vary. Per-stage allocation and
planning/validation details are preserved in the JSON rather than inferred from
total latency.

The measured desktop Edge 120x120 inspection rebuilds 14,400 Cells in about
541–544 ms; mobile-width final preview measured 556 ms on the same machine.
50x50 nested inspection measured about 63–65 ms. These are synchronous DOM rebuild
measurements, not frame-rate guarantees or whole request latency. Desktop/mobile
Edge checks covered disclosure, Door before/after geometry, zoom, regeneration,
final DELVE1 export and Designer validation. No console or failed-request errors
were reported. The known Three.js degradation near 80x80 is separate and unchanged.

Planning is bounded candidate sampling plus small local flood/split work. Terrain,
Edge and reservation reads use indexed board lookups. Reanalysis is window-sized;
preview retention scales with a fixed number of top-level frames plus sparse
accepted edits/semantic spans. No global scan, streaming, chunks or persistent
world state is introduced. Window-local results need not match overlapping crops.

## Verification and remaining limits

Focused suites cover rollback/staleness/conflicts/content, budgets, dense and Water
preparation, independent Building/Room use, partitions/entrances/furniture, parent
failure after children, nested seeds, semantic roles, opportunity-driven Camps,
Edge-only topology invalidation, complete trace replay at maximum sizes, recipe
roundtrips, preview state and final HTTP export/Designer equivalence. Existing
DNG1/DNG2 fingerprint tests remain unchanged. Independent reviews covered planning,
construction, determinism, content, preview replay and performance.

Final automated checks:

| Check | Result |
| --- | --- |
| Solution build | Passed, zero errors; existing cached-audit NU1900 warning |
| Focused construction/orchestration Engine checks | 58 passed |
| Focused World HTTP integration | 5 passed |
| Focused preview JavaScript | 22 passed |
| Full Engine, including DNG1/DNG2 regressions | 2,477 passed |
| Full Web | 682 passed; 12 existing content-reference failures |
| Full JavaScript and dispatcher security | 961 passed |
| Desktop/mobile Edge | Passed; final Designer validation succeeded |

The twelve Web failures are the already established `ScenarioContentReferenceTests`
conflicts for Equipment `knightly-boots`, documented in WORLD_GENERATOR.md and
TERRAIN_ENRICHMENT.md. No unrelated content/tests were changed to suppress them.
The isolated browser host was stopped; the existing 5080 game was preserved.

Reproduce from the repository root with restored dependencies:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --civilization --fixtures .playwright-cli/civilization-fixtures
python tools/render-civilization-gallery.py .playwright-cli/civilization-fixtures assets/previews
```

Run benchmarks without concurrent builds/tests/browser work. Resolve installed
Node/Pillow-capable Python and set `PROJECTDELVE_PYTHON` as documented in AGENTS.md.
Browser inspection uses the playwright-cli skill and protected dispatcher described
in BROWSER_INSPECTION.md, with a disposable 5081 host preserving the 5080 game.

Buildings currently use bounded rectangular BSP interiors, and Settlements use
four possible lots around a shared street. Variety is procedural rather than
blueprint-based, but larger/irregular architecture remains future work. Preparation
does not automatically widen Doorways for large Units or connect isolated Sites.
Semantic output remains generator-internal/preview-only and is not persisted as
scenario gameplay state. Connections and Population consume these references and
validate their own requirements; Objectives and Playability remain separate. Fortifications,
Dungeons, blueprints, generative Designer commands and new mechanics are deferred.
