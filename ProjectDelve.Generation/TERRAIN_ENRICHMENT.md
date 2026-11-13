# Terrain analysis, enrichment and inspection

World Generator now runs an explicit typed recipe: Natural Terrain, Terrain
Analysis, Forest Enrichment, Rock Enrichment, Water Enrichment, Grassland
Enrichment and Post-Enrichment Analysis. Disabled families are omitted. Analysis
never paints terrain. The output remains an ordinary terrain-only scenario.
This milestone introduced no Units, Sites, Objectives, climate or inter-site routing.
The subsequent optional Civilization stage is documented in
[CIVILIZATION_SITES.md](CIVILIZATION_SITES.md); it runs after enrichment and before
final analysis, and adds constructed terrain and Edges with semantic Site results.

## Scope and recipe contract

Natural Terrain's original stateless sampler is unchanged and remains globally
seamless. Enrichment is **WindowLocal**: moving/resizing a window can change
formation grouping, feature placement and local identities. Neither enriched
overlaps nor world-spanning formation IDs are promised. Maps remain bounded to
120 by 120. Partial formations are marked when they touch the requested window;
the analysis does not claim to have discovered their complete world extent.

The current WLD1 development format accepts an explicit suffix before the seed:

```
WLD1-120x100-X-17-Y-13-F15-W8-R50-KBroad-HMixed-E1-I2-B1111-S123456789ABCDEF0
```

`E` enables enrichment; `I` is intensity 1..3; the four `B` bits select Forest,
Rock, Water and Grassland. The browser defaults to enabled, medium intensity and
all families; presets supply editable effective values. E0, or an omitted
suffix, produces natural terrain plus read-only analysis. The suffix retains
family/intensity settings even when enrichment is disabled. Completed codes
encode values rather than mutable preset identities. There is no WLD2 migration
layer; the World Generator is under active development. DELVE1 preserves a
concrete map. DNG1/DNG2 interpretation, streams and output are unchanged.

`WorldGenerationPlan.FromRecipe` constructs the normal schedule. The internal
typed plan also supports distinct IDs for repeated enrichment steps and optional
post-analysis steps. It is not accepted as executable HTTP/JSON content and has
no external recipe DSL/editor. A plan has at most 24 steps. Validation requires
unique ASCII IDs, supported parameter types/intensities, earlier dependencies,
Natural Terrain once and first, and original analysis once before enrichment.
Each consumer must transitively depend on the preceding terrain writer. Steps
execute in declared order; dependencies are validated, not silently reordered.

The root seed belongs to the world. Natural Terrain uses that root directly to
preserve its sampler contract. Other steps derive independent SHA-256 seeds
from root + stable Step ID + stage identity + implementation namespace, never
step position. Formation-local feature streams use the existing PCG primitive
with a separate World namespace; DNG stream helpers are untouched. Independent
insertion of a read-only step and removal of later steps are regression-tested.

## Context and metadata

`WorldGenerationContext` owns original samples, mutable output samples, original
analysis, current analysis, features, entrances, opportunities and routing work
counters. Each generation starts a fresh context. Terrain changes replace output
records; original samples and original formation IDs remain provenance.

| Record | Meaning |
| --- | --- |
| `TerrainAnalysisSnapshot` | TAN1 source fingerprint, window/revision, indexed terrain/logical/walkable labels, depth, clearance, boundaries, chokepoints, narrow passages, formations and opportunities. |
| `NaturalFormation` | Window/source-scoped ID, family, observed bounds/core/envelope area/density, maximum depth, partial flag, contacts, field means and LOS-blocking count. |
| `EnrichmentFeature` | Step and source formation, concrete feature kind, approximate bounds, changed count and entrance references. |
| `EntranceCandidate` | Feature-associated cardinal portal with two genuinely passable endpoints and usable width. |
| `TerrainOpportunity` | Clearing/chamber/island/open area/shore/landmark or bridge candidate, anchor/bounds/usable area, provenance and current walkable component. Bank-pair candidates also expose opposite bank and Water span. |
| `ConnectivitySummary` | Walkable area, component sizes/count and largest connected area. |

All spatial arrays and references use local row-major indices. Coordinate
conversion uses the signed window offset. Bounds are approximate envelopes,
not guaranteed rectangular building footprints. A future Site stage must query
actual current terrain/clearance and validate its placements. `RequiresConnection`
explicitly marks isolated islands and unbuilt crossings; its absence is not a
guarantee of reachability from a future start location. Relevant future Sites and
Units must be checked by the later Playability stage.

Original IDs include a stable source fingerprint, window scope, kind and absolute
anchor. They survive modifications within that context, including rock split by
tunnels or water interrupted by islands. Current labels are revision-scoped and
may split/merge on reanalysis. There is no persistent/global identity matching
or region-history framework. Planned Sites and actual structures are not natural
formations, and are not implemented here.

Exact shapes remain in shared raster indices and concrete terrain. Metadata does
not duplicate complete Cell lists per record. Boundaries and contacts can be
queried from the original labels. Generator metadata is absent from gameplay
state and DELVE1. Designer retains its established final definition/code source
snapshot; editing that draft does not retain or refresh generator analysis.

## Analysis algorithms

Exact components use four-neighbour flood fill by terrain identity. Logical
components combine Grass/Dirt and group forest/rock cores across a single
intervening Grass/Dirt Cell with matching cores on opposite sides. A gap remains
ground, contributes to envelope area and never becomes a walkable forest/rock
Cell. Grouping cannot cross Water or another blocking family. Diagonal touching
does not count as an ordinary terrain connection. This deliberately conservative
small-gap method is not arbitrary large-radius morphological closure.

Multi-source distances measure terrain-family interior depth and walkable
clearance, including map edges. Passability and LOS come from resolved typed
terrain definitions. Optional Edge input blocks Walls/Windows, treats Doors as
potential passages and seeds clearance alongside blocking Edges. Connectivity
ignores transient Units, Phase, movement allowances and FoW. It describes
ordinary 1x1 terrain topology, not legal destinations for every Unit type/footprint.

Iterative Tarjan traversal identifies articulation Cells without overflowing on
a 14,400-Cell snake. Separate opposing-obstacle scans group one/two-Cell narrow
passages, including two-Cell necks without articulation points. Crop boundaries
alone do not create such passages. Exact cuts are reported as facts; no tactical
importance or defence-site ranking is inferred.

Analysis runs again after each enrichment step and at the end. This keeps every
inspection frame's walkable labels current. Feature opportunities are remapped
to current components while their original formation provenance is retained.

## Concrete enrichment

| Family | Transformation | Cumulative intensity ceilings |
| --- | --- | --- |
| Forest | Connect suitable existing clearings and deep irregular grassy openings to meaningful current land regions; add exposed-soil trails and measured useful branches/shortcuts. | 8 / 13 / 18% of original formation core changed. |
| Rock | Interior irregular floor chambers, connected tunnels, selected mouths and occasional second-mouth mountain passes. | 7 / 10 / 13% excavated. |
| Water | Interior irregular islands and occasional loose chains; shoreline anchors and bank-pair crossing candidates. | 3 / 5 / 7% converted. Original shoreline Water Cells remain unchanged. |
| Grassland | Spaced tree pockets, rock outcrops, Dirt patches and nearby natural-landmark opportunities. | 4 / 6 / 8% changed overall; blocking additions limited to 1 / 1.5 / 2%. |

These are upper disturbance bounds, not coverage targets. Small/thin formations
can be skipped. Candidate/feature/route counts are bounded. Repeated steps share
the original formation's edit budget rather than resetting it.

Irregular shapes are perturbed ellipses trimmed to their center's cardinal
component. Weighted routes use indexed grids, deterministic priority ties and
predecessor reconstruction. Coherent spatial costs and bounded off-axis bends
reduce repetitive straight trails. A complete room/opening plus route must fit
the remaining budget before committing. Forest writes only original Tree cores;
rock writes only original Solid Rock; islands write only interior Water; grassland
writes unchanged original Grass/Dirt. Fixed source masks and ordered commits
resolve overlap deterministically.

Rock keeps chamber buffers and avoids Water-adjacent excavation and incidental
contacts with unrelated natural cavities. Explicit selected mouths may connect
previously isolated natural spaces. Caves are ordinary floor within the
single-layer board, not underground terrain beneath a second surface. Entrances
are checked after carving. All-rock/all-forest crops do not invent entrances at
their artificial map boundaries.

Islands keep Water gaps and are not automatically connected or populated.
Bridge candidates retain two passable banks and their intervening Water span;
they are opportunities, never claimed traversable entrances. Grassland protects
both banks, previous entrances and useful spaces, and provisionally rejects
blocking clusters that split an existing walkable component. It need not increase
walkable area: preserving open space while adding occasional cover is intended.

This stage creates local exploration connections within formations. Deliberate
routes between future Sites remain the responsibility of Connections. No final
scenario reachability or objective guarantee is made.

### Forest selection, Phase 2

Forest uses a decision-local index of ordinary cardinal land components. A
meaningful approach has at least 64 Cells, or the area of the largest available
approach if none reaches 64. This is a bounded authoring preference, not a new
game rule or a requirement to join the world's largest component. Several
disconnected meaningful regions can each receive features. Small isolated
regions remain possible, and an all-Tree crop still has no fabricated mouth.

Existing clearing candidates must route to a *different* current component;
a mouth back into their own pocket cannot count as an external connection.
New openings route to eligible approach regions using one deterministic
multi-target weighted search. Mouth eligibility follows resolved ordinary land
permissions, including Wet Grass and Rocky Water. Traversed ground is preserved;
only still-Tree Cells of the original Forest core can be carved. An existing
clearing already in a meaningful region is not credited again for a local loop.

Tree candidates rank by `4 * remaining depth + original depth`, without the old
depth-five plateau. Depth is recomputed over the formation's indexed core after
acceptance. Existing disconnected clearings score `24 + min(40, usable area)`;
the existing 4..80-Cell clearing candidate range is retained. Seeded ordering
settles equal candidate scores; `(cost, Cell index)` settles route ties. A
seven-Cell Manhattan exclusion around accepted centers (also from previous
steps) and a three-Cell exclusion around attempted centers avoid repeated nearby
selection. These are exclusion radii, not a uniform corridor grid. Irregular
blobs and coherent route-cost variation retain seed-dependent shapes.

After accessibility selects a main mouth, paths of at least ten Cells may use
one additional search with the same soft sinusoidal guide used by ForestTrail
route planning: lateral amplitude is at most four Cells and tapers to zero at
both endpoints. The shaped path must fit the same remaining budget and contain
at most `1.3 * original path Cell count + 4` Cells; otherwise the original
connection is retained. Branch paths also use the soft guide. No mandatory
waypoint, new seed domain or extra RNG draw is introduced. This avoids the
long rectangular patterns seen with unshaped cardinal cheapest paths, while
allowing straight segments where terrain or budget favors them.

A separated second mouth is considered on one third of candidates. It must
add terrain and either join a component not already touched by the proposal or
shorten the actual pre-branch walk by more than eight steps. At most four
alternatives are tried. The complete clearing and accepted paths must fit the
remaining original disturbance allowance before any terrain or metadata writes.
`RequiresConnection=false` describes that accepted local connection; it is not a
promise of access from Quick Play's final start, or through later construction.

Per formation the pass accepts at most `min(64, 2 + core area / 250)` features
and tries at most `min(256, 16 + 4 * feature limit)` centers (integer division),
with at most six route searches per center (main, shaping, four branches).
Main/branch routing and shortcut checks share a two-million-Cell expansion cap
per Forest step; exhausted work cannot certify a shortcut. Original membership
and changed counts are grouped in one board scan. Incremental component unions,
indexed spatial exclusions and reused route scratch arrays avoid per-candidate
full-board analysis. Candidate/depth work is scoped to the indexed formation,
bounded by the above limits; remaining-depth refresh runs only after acceptance.
Search may stop with unused disturbance budget. Intensity still selects only
the 8/13/18% ceilings; it does not promise monotonic feature counts or layouts.

Forest remains an early sample transformation. Plan validation prohibits it
after planning-board construction; it cannot edit Sites, reservations, Edges,
Machines or Resources. Geographic formation analysis already treats ordinary
Closed and Open Doors as equivalent potential passages, without changing Door
state. Engine movement and actor-specific Accessibility Analysis V1 retain their
separate semantics. No late pass or final connectivity repair is added.

See [Phase 2 measurements](FOREST_ENRICHMENT_PHASE2_RESULTS.md) for the paired
Forest-only corpus, performance, remaining limitations and verification.

### Rock selection and dungeon compatibility

Rock shares the indexed land-component and bounded search implementation used by
Forest, with its own Solid Rock source mask, dry-cell restrictions and seed domain.
The Forest algorithm and output are unchanged. Meaningful approaches have at
least 64 ordinary land Cells, or the largest available approach's area if none
reaches 64. This preference does not force a single universal component.

Candidates rank by `4 * remaining depth + original depth`, with seeded ties and
depth refreshed after each accepted feature. Attempted centers exclude a radius
of two Manhattan steps; accepted centers exclude seven, including earlier steps.
Each feature combines an irregular chamber of at least seven Cells with a route
to a meaningful mouth or an already accessible interior tunnel. The full edit
must fit the original cumulative 7/10/13% ceiling before any terrain or metadata
is committed. Reuse is cheaper than new excavation.

Long paths can use a soft sinusoidal guide of amplitude at most three Cells;
the shaped route must fit the same budget and length bound `1.3 * original + 4`.
Only its selected endpoint may cross the original formation boundary. On half
the candidates, up to four separated alternative mouths are considered, favoring
other existing components. A same-component branch must shorten the actual walk
by more than eight steps. A useful pass can reuse an earlier mouth; feature kind
records the measured branch benefit, not merely two newly recorded entrances.

A conservative local guard surveys exact remaining Rock masses. A mass initially
offering two to four greedily packed buffered 3x3 room positions retains that
capacity in one remaining mass; a larger opportunity retains at least two. This
protects narrow candidates without reserving whole mountains. It is a geometric
preference, not a promised dungeon: entrance geometry, room profiles, bounded
layout search and construction permissions still determine actual placement.
Mountain Dungeons can additionally absorb eligible earlier floor under the
[controlled absorption policy](MOUNTAIN_DUNGEONS_V1.md).

Per formation, accepted features are bounded by `min(48, 2 + core area / 300)`
and candidate attempts by `min(192, 16 + 4 * feature limit)`. Main routing, shaping,
branches and shortcut checks share two million visited Cells per Rock step.
Original cores and changed counts are indexed once; depth and opportunity checks
scan only the relevant formation/mass. Routing counters exclude those local
survey/selection scans. No late enrichment or final connectivity repair is added.

See [Rock phase measurements](ROCK_ENRICHMENT_PHASE3_RESULTS.md) for the paired
corpus, thin-mountain regressions, runtime costs and remaining limitations.

## Inspection and evaluation

The server retains compact palette-encoded terrain strings for each frame and
indexed overlays, not seven independent scenario/game states. Previous/Next and
the stage selector inspect these frames; Before displays the preceding frame.
Changes belong to the selected transformation, while component diagnostics and
timings follow the displayed frame. Overlays show original boundaries, current
components/chokepoints, entrances and opportunities. Click or hover to inspect.
Zoom changes layout without remounting Cells; stage/overlay changes rebuild one
bounded preview grid and retain board scroll offsets.

Copy Completed Code, DELVE1 and Designer always use the retained final result.
They never export an inspection frame. Editing settings keeps the previous
preview marked outdated; stale/cancelled requests cannot overwrite it.

Visual evaluation includes the ten original fixed-seed landscapes, all five
presets at 120x100 and 120x120, a mixed 120x120 crop, and a negative-offset awkward
45x70 crop. Pair comparisons share identical underlying natural samples.

- [Original seed corpus before/after](../assets/previews/world-enrichment-fixed-seeds.png)
- [Forest stage](../assets/previews/world-enrichment-woodland.png)
- [Rock stage](../assets/previews/world-enrichment-rockywilds.png)
- [Lake stage](../assets/previews/world-enrichment-lakecountry.png)
- [River stage](../assets/previews/world-enrichment-rivervalley.png)
- [Grassland stage](../assets/previews/world-enrichment-grassland.png)
- [120x120 comparisons](../assets/previews/world-enrichment-120x120.png)
- [Awkward boundary crop](../assets/previews/world-enrichment-boundary-crop.png)
- [Recipe/fingerprint manifest](../assets/previews/world-enrichment-gallery.json)

These comparison sheets use a diagnostic terrain palette. Separate Edge captures
verify actual Enhanced/Classic controls, mobile layout, overlays and Designer.
Visual review prompted bounded bends for overly straight axis-aligned trails;
conservative editing limits were retained. There are no shape quotas or uniform
map-wide accessibility targets.

Final Edge evidence: [desktop preview](../assets/previews/world-enrichment-desktop.png),
[overlays](../assets/previews/world-enrichment-overlays.png),
[Designer handoff](../assets/previews/world-enrichment-designer.png),
[mobile preview](../assets/previews/world-enrichment-mobile.png), and
[mobile zoom/pan](../assets/previews/world-enrichment-mobile-zoom.png).
[Browser evidence and data-only plans](../assets/previews/world-enrichment-browser.json)
record the final clean runs. The desktop viewport is 1440x1100 and mobile viewport
390x844 on installed Windows Edge, not physical phone hardware. Preview DOM
construction measured 362.8 ms for the selected 120x100 desktop frame and 456.8 ms
for the 120x120 mobile frame. These timings exclude decode/paint/network and are
not frame-rate measurements. Stage changes still rebuild the bounded preview;
zoom only changes CSS. Both runs reported no console errors/failed requests.
Designer validation reported Valid after transfer while an intermediate stage
had been inspected, and Generate Another returned a new completed seed.

## Performance and verification

Detailed measurements are in
[world-enrichment-performance.json](../assets/previews/world-enrichment-performance.json).
They use detached .NET 10 Release, three warmups and medians of seven generations,
all five presets, root seed 123456789ABCDEF0 and offset (-17,-13). Each step includes
its frame projection and, for enrichers, current-terrain reanalysis. Total includes
ordinary scenario validation and serialization/fingerprinting. Finalization is
reported separately and includes projection, Engine creation, serialization and
hashing; it is not a pure materialization measurement.

Observed ranges across the five fixed-seed presets (decimal MB):

| Size | Total generation median | Allocated/generation | Preview UTF-8 | Retained managed preview |
| --- | ---: | ---: | ---: | ---: |
| 50x50 | 25.9–35.6 ms | 6.82–7.65 MB | 0.077–0.115 MB | 0.120–0.144 MB |
| 120x100 | 56.2–86.8 ms | 31.68–63.31 MB | 0.368–0.576 MB | 0.556–0.687 MB |
| 120x120 | 58.8–69.6 ms | 35.03–46.46 MB | 0.429–0.694 MB | 0.658–0.819 MB |

At 120x120, Rocky Wilds has 63 route searches / 9,972 visited Cells; its stage
medians are Natural 6.24, original Analysis 5.06, Forest 9.70, Rock 16.79,
Water 6.20, Grassland 8.22 and final Analysis 5.27 ms. Stage medians need not
sum exactly to the total median. This seed's walkable area rises 3,486→4,041;
largest component rises 1,072→1,265. These are examples, not acceptance quotas.
Grassland may reduce walkable area, and local exploration need not enlarge the
map's previously largest component.

Per-stage allocated bytes and routing searches/visited Cells are recorded. Raw
preview estimates, UTF-8 payload size and a separately measured managed retained
preview heap delta are distinct figures. The heap delta retains the preview alone
after generation and forced collection; it is an approximate managed measurement,
not browser/process/GPU memory. Browser session cloning and mounted DOM have
additional costs.

Component/depth/boundary/passage analysis is linear in Cells plus contacts.
Weighted route searches are O(N log N) with bounded attempts and O(N) scratch
space. Grassland performs a bounded number of linear component checks. Terrain
and content properties are indexed within the generation scope; no Engine
storage refactoring or persistent cache was introduced. Preview work scales with
Cells times retained steps. A larger world would require selected-window analysis
and an explicit ownership/continuity contract, not a complete global scan.

Enrichment currently gathers each eligible formation's core with a window scan
(O(FN)); label-index grouping is a possible later optimization if substantially
more fragmented input becomes important. No all-pairs route computation is used.

Final automated verification:

| Check | Result |
| --- | --- |
| Solution build | Passed, zero errors; cached dependency auditing emits existing NU1900. |
| Focused analysis/enrichment/orchestration | 43 passed. |
| Full Engine including unchanged DNG regression corpora | 2,425 passed. |
| Full Web | 681 passed; 12 existing `ScenarioContentReferenceTests` failures involving conflicting `knightly-boots` Equipment. |
| Full JavaScript and dispatcher security | 956 passed. |

The twelve Web failures match the independently established baseline documented
in WORLD_GENERATOR.md and LARGE_MAP_SUPPORT.md. No content or unrelated tests
were changed to suppress them. Focused HTTP checks verify reproducible frames,
typed Workspace content, final DELVE1/Designer equivalence and unchanged live
game state. Synthetic fixtures cover gaps, diagonals, walls/doors, depth, a full
snake, chambers/entrances, water preservation, crossing-bank protection, repeated
steps, seed/position independence, changed overlays and maximum-size maps.

Reproduce from the repository root with restored dependencies:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.Engine.Tests --no-build --no-restore --artifacts-path .playwright-cli --filter "FullyQualifiedName~Enrichment|FullyQualifiedName~TerrainAnalysisTests|FullyQualifiedName~WorldGenerationPipelineTests"
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --enrichment --fixtures .playwright-cli/enrichment-fixtures
python tools/render-enrichment-gallery.py .playwright-cli/enrichment-fixtures assets/previews
```

Resolve installed Node/Pillow-capable Python as documented in AGENTS.md. Browser
inspection follows BROWSER_INSPECTION.md, with an isolated 5081 host and disposable
Edge contexts; the existing 5080 game is preserved. Independent reviews covered
terrain correctness, seed/dependency isolation, preview state and measurement
semantics. Review fixes include wall-aware clearance, per-component opportunity
bounds, comparison-frame diagnostics, protection of both crossing banks and clear
finalization timing labels.

The reported Three.js smoothness degradation near 80x80 remains a separate
rendering-performance concern. This milestone does not change Three.js rendering.
Global enrichment, climate, inter-site connections, population,
objectives, final Playability, chunking and expanding worlds remain unimplemented.
