# Connections and route networks

Current abundance settings and scaling policy: [Density Scaling V1](DENSITY_SCALING_V1.md).
Older numerical recipe examples and performance corpora below describe historical configurations.

The subsequent [visual quality refinement](ROUTE_QUALITY.md) documents current
cost tuning, paired galleries, diagnostics and verification. Measurements below
describe the original route milestone baseline.

World Generator supports optional Primary Routes before Civilization, Site
Connections afterward, and selected Exploration Links. These are WindowLocal
authoring stages. Through routes can enter and leave the map with no Sites.
Constructed crossings replace Water with ordinary existing walkable terrain;
tunnels are ordinary floor through Solid Rock. No Engine mechanics, layers,
bridges with Water beneath them, or DNG1/DNG2 algorithms were added or changed.

## Execution and ownership

`WorldGenerationPlan` supports repeated `RouteNetwork` steps with explicit
`RouteNetworkMode` parameters and distinct stable Step IDs. Every consumer depends
on the preceding terrain/Edge writer. Routes require original analysis; the
sample-based enrichment stages finish before planning-board writers begin.
The normal enabled schedule is:

```text
Natural Terrain -> Original Analysis -> Enrichment
  -> Primary Routes -> Civilization -> Site Connections
  -> Exploration Links -> Current Analysis
```

Disabled stages are omitted. Typed plans may repeat route steps without an
external DSL or interpreter. Mode parameters, not display names, choose behaviour.

`WorldGenerationContext.EnsurePlanningBoard` initializes one indexed board at the
first construction writer and retains it through later stages. Civilization no
longer reconstructs it and loses reservations. Original natural samples and
formation provenance remain independent of materialized output.

`PlanningState` keeps hard owner reservations separate from route memberships.
Several routes may share a Cell. Ordinary construction cannot overwrite or
reserve protected route corridors. Route drafts may traverse compatible existing
corridors while preserving their terrain and open Edges. Memberships are copied
and committed atomically with terrain/Edges and never transfer Site ownership.

`RouteAccessGrant` is an attempt-local exact-owner permission: traversal Cells,
terrain-write Cells and Edge-write keys are separate. Both Edge endpoints need
access; every foreign owner needs the exact Edge grant. Protected sentinel Cells
cannot be granted, and owner prefixes confer no rights. Public planning views are
read-only; concrete generators mutate private drafts.

Each complete RoutePlan stays private until its parent accepts it. A route failure
leaves no terrain, Edge, reservation, content or membership residue. Accepted
network children compose into a stage draft; its complete board patch passes
Engine validation before routes, operations and semantic results are published.
Stale or unrelated patches retain their existing rejection behaviour.

## Independent Route Generator

`RouteGenerator.Plan(RouteRequest, GenerationScope)` returns a typed
`PlacementResult<RoutePlan>`. Requests separate endpoints, profile, hard
`RouteRequirements`, soft `RoutePreferences` and exact access grants. Start and
destination may be sets of Cells: this supports cost-aware attachment to an
existing network, not just attachment to its nearest geometric point.

The planner uses deterministic four-neighbour weighted A*. State includes the
incoming direction. An obstacle-free target-distance field supplies an admissible
heuristic. Traversal costs stay positive, including route-reuse discounts.
Terrain properties and full-width reuse are indexed before searching; expansion
does not consume RNG. Coherent variation uses rotated, nonseparable spatial fields
to avoid preferred rows and columns across uniform terrain.

| Profile | Initial policy |
| --- | --- |
| ForestTrail | Dirt, width 1, stronger coherent wandering, low turn cost, Tree clearing without Rock excavation |
| MinorPath | Dirt, width 1, moderate directness and reuse, no default Rock excavation |
| Road | Gravel, width 2, higher turn cost, clearing and bounded Rock excavation |
| RockPassage | Stone Floor, width 1, bounded excavation with an added Water-neighbour cost |

Available passable ordinary ground is selected without new content. An explicit
unsupported surface fails before attempts. Rail is rejected as a new surface
because it requires authored direction; existing passable Rail, production and
assemblies are preserved during traversal. Unknown blocking fixed geometry is a
hard exclusion. No Phase or Flying is used.

The complete corridor is a union of width-by-width anchor placements along the
path. Search checks every square's Cells/internal Edges and each cardinal step;
materialization checks them again after preparation. This preserves turns and
allows explicit 2x2 footprint validation. Width supports 1..3; required footprint
supports 1 or 2. There is no automatic width reduction at blocked passages.

Closed Doors are blocked by default. An independently supplied request may allow
potential Door traversal; accepted metadata then explicitly records
`RequiresOpeningDoors`. Open board topology and Doors-openable topology are
reported separately. Neither is a promise for every future Unit capability,
occupancy arrangement or movement pattern.

Hard limits include allowed area, complete corridor length, changed terrain,
weighted preparation cost, Water Cell count, Water-run length, forbidden Cells,
attempts and expanded states. The planner validates supplied world-window bounds
and board dimensions before routing. Single-column cardinal geometry is supported.
Maximum search attempts are three; default per-attempt expansion limit is 60,000.
Network stages share 128 attempts and two million planning work units.

Preparation uses existing terrain/Edge legality. Authorized blocking Edges can be
removed in the private patch; Structures and Site Edges cannot be opened merely
because a high route cost was accepted. Water is preserved unless enabled. The
full widened crossing must satisfy both Water-count and consecutive-span limits.
Crossing records contain exactly the former Water Cells. Rock mouth records
identify actual passable transitions between excavated Rock and other ground;
boundary ports represent crop entry/exit without inventing off-board Edges.

## Network structure and Civilization

Primary route requests have stable slots and independent boundary streams.
Opposite sides provide separated entry/exit bands; endpoint choice and terrain
costs determine actual ports. Multiple routes alternate their main axis, with
seeded positions and compatible reuse. Very narrow maps choose their usable long
axis; tiny maps report an unresolved request instead of claiming a useful crossing.
Routes need not pass through the map centre or visit a Site.

Civilization receives existing corridor anchors as soft proximity/setback
preferences. Forest Camps orient their apron toward nearby routes. Settlements
rank candidate areas by preparation/proximity cost and publish actual reachable
exterior approaches toward routes. Their Building layout and internal paths remain
Settlement-owned. Sites may still be constructed far from a route.

`SiteAccessPort` records an exact exterior approach, boundary anchor, outward Cell,
direction, verified width and exact-owner permission. Publication flood-checks
existing open exterior space. Its outward Cell is reserved when available, before
the final Site patch is captured, so later Sites cannot occupy the connection
apron. It never reserves outside the supplied placement area or takes ownership of
an existing route corridor. A Site with no publishable port records a connection
requirement that later resolves as unavailable, not a fabricated entrance.

Site Connections consume `SiteConnectionRequest` and the published port. Branches
start at the outward attachment and route to a compatible network target set.
Without Primary Routes, a stable eligible Site becomes a hub. Requests identify
the specific port: reaching one hub port does not satisfy another. Route keys
derive from stable request identities, including multiple requests for one Site.

Ordinary branches preserve internal Site floors/paths, Walls, Doors and furniture.
Only published outward preparation is authorized. Some optional Campsites remain
remote by deterministic policy; explicit remote and required requests are
supported in typed Site metadata. A requested connection can remain unresolved
because of barriers, permissions or budgets. No universal connectivity or scenario
Objective is inferred.

Exploration Links consider disconnected useful spaces and selected route detours
or bottlenecks. Region candidates are deduplicated by component and connectivity
is rechecked against the current private draft. A proposed loop needs useful new
corridor geometry plus a shorter network route or a validated bypass. Straight
open roads do not receive loops to meet a quota. Accepted quality records qualify
cycle creation, previous/new distance, shared/new corridor, selected network-cut
bypass and additional region access. Independent Site entrances are not claimed
merely because two routes reach the same apron.

Route disturbance ceilings accumulate across route stages: intensity times 1000
changed Cells, 400 excavated Rock Cells and 36 filled Water Cells. Individual
requirements also cap cost and width-expanded modifications. These are ceilings,
not targets, and are independent of natural enrichment and Site preparation limits.

## Metadata and determinism

Three meanings remain separate:

* `RouteIntent`: requested link, accepted/unresolved/remote/rejected status and reason.
* `RouteInfo`: accepted corridor, compressed world-coordinate runs, ports,
  preparation, crossings, mouths, width/footprint facts and materialization identity.
* `RouteNetworkSnapshot`: shared segments, junction contacts, Site attachments,
  alternatives and revision-qualified actual route/board connectivity.

Corridors and contacts use compact exact row spans. World centreline geometry uses
cardinal runs. Shared Cells retain multiple route memberships. Geometry is not
repeated as full Cell lists in every semantic record. Route identity differs from
the source-revision materialization ID and transient component labels.

All route randomness derives from the root seed, stable Step/window/request path,
`Routes1` namespace, purpose and deterministic attempt. Selection, endpoints,
variation, remote decisions and alternatives have independent streams. IDs do not
use accepted-route counts or expansion order. New later requests do not reseed
earlier requests; changed terrain/claims may change feasible later placement.
Every generation starts from Natural Terrain.

All geometry and materialization remain WindowLocal, even though geometry is
stored in world coordinates. No adjacent-window matching or persistent/global
route identity is promised. Natural Terrain remains globally seamless.

## Controls, preview and export

The optional effective recipe suffix, after Civilization and before the seed, is:

```text
-Q1-TMedium-JRoad-L1-AMedium-U0-Z2
```

`Q` enables routes; `T` is Through route density; `J` is ForestTrail, MinorPath, Road
or RockPassage; `L` enables Site Connections; `A` is Exploration link density.
Both densities use None/Low/Medium/High/VeryHigh, with the bounded formulas in
[Density Scaling V1](DENSITY_SCALING_V1.md).
`U` permits ordinary-terrain Water crossings; `Z` is intensity 1..3. Presets provide
editable profile defaults; completed codes capture effective settings. Natural-only,
enrichment-only, routes without Sites and Sites without Primary Routes remain
available. DNG recipes are unchanged.

Top-level stage frames include Primary Routes, Site Connections and Exploration
Links. Accepted operation traces retain sparse edits and explicit
`ConstructionBatch` checkpoint/range metadata. Replay begins after that checkpoint,
not at the start of an accumulated trace. This handles edits across consecutive
route/construction stages and inspection from the final analysis frame.

Server-authored overlays show accepted runs, corridor spans, boundary/Site ports,
junctions, shared segments, crossing fills, tunnel mouths and exact access geometry.
Intended endpoint pairs are explicitly labeled as requested links, not computed
paths. Accepted-stage metadata remains labeled while inspecting earlier steps.
Nested diagnostics show costs, guarantees, unresolved requests and alternative
quality. JavaScript renders data; it does not reproduce route/connectivity algorithms.

DELVE1 export and Designer always use the final accepted scenario, independently
of selected stage/operation. Zoom, pan, new-seed regeneration, recipe copying and
the existing Site/Building/Room inspector are retained.

## Evaluation, performance and verification

`tools/WorldGenerationBenchmark --routes` profiles all five presets at 50x50,
120x100 and 120x120, plus Mixed, no-Site Through Routes, rock passages, narrow and
Site-network-only cases. It uses Release, three warmups and five samples per case,
without concurrent test/browser work. `world-routes-performance.json` preserves
per-stage time/allocations, accepted search states/queue peaks, total planning
attempts/work including failures, materialization/Engine-validation costs, raw
preview estimate, measured retained managed preview and UTF-8 response sizes.
Planning time includes unsuccessful planning; accepted materialization is measured
separately. Network search-state counters describe accepted plans; total work and
attempt counters also include rejected attempts.

Final local Release results (ranges of the five standard preset medians; decimal
MB, 2026-11-06):

| Board | Generation | Allocated per generation | Retained managed preview | Response payload |
| --- | --- | --- | --- | --- |
| 50x50 | 83–330 ms | 58.0–72.9 MB | 0.350–0.388 MB | 0.697–0.789 MB |
| 120x100 | 245–416 ms | 163.1–246.3 MB | 1.031–1.187 MB | 1.284–1.865 MB |
| 120x120 | 318–480 ms | 212.2–275.8 MB | 1.234–1.477 MB | 1.562–2.263 MB |

These cover the complete enabled pipeline, including enrichment and Civilization.
Allocation totals are transient allocation traffic, not retained memory. The
fixture response includes terrain definitions and DELVE1 as well as preview data;
it is an uncompressed UTF-8 size, not a measured network transfer size. Exact
per-stage timings, route planning/materialization, Engine validation, search work,
retry counts, changes and the five additional cases are recorded in
`assets/previews/world-routes-performance.json`. Validation and repeated
topology/metadata construction are material costs; the milestone leaves further
optimization to measured follow-up work.

The implementation uses indexed terrain properties, anchor costs and compatible
reuse masks. It avoids per-expansion full-width reuse enumeration. Search is
O(N log N) for bounded heading states; topology/reanalysis is window-sized. Full
Engine validation occurs at accepted network batches, not every A* expansion.
No Engine storage/pathfinding redesign, chunking or streaming was introduced.

`tools/render-routes-gallery.py` creates before/first-route/final comparisons and
close-ups from server fixtures. The gallery covers Grassland, Woodland, Rocky
Wilds, Lake Country, River Valley, Mixed, Through-only, tunnels, crossings, narrow
windows and independent Site networks. Visual review replaced axis-separable
variation fields that produced overly rectangular forest trails with rotated 2D
variation. Road directness remains distinct from forest trails.

Focused fixtures cover permissions, rollback/staleness, conflicts, exact grants,
terrain/Edge validity, width and 2x2 turns, Water preservation/filling, tunnel
mouths, deterministic seeds, boundary ports, multiple requests/hub ports, region
deduplication, useful loops and server/client multi-stage reconstruction. HTTP
tests cover final terrain/Edges, DELVE1, Designer validation and unchanged live game.
Existing DNG regression coverage remains intact.

The completed six increments were checked in order: shared planning/checkpoint
replay, independent routing, Through Routes, Civilization attachments, useful
alternatives/crossings, then integrated inspection and evaluation. Independent
reviews covered ownership/grants, terrain and Edge legality, seed isolation,
access-port identity, width validation, performance and checkpoint reconstruction.
Review findings were corrected with focused regression coverage before final runs.

Final verification (2026-11-06):

| Check | Result |
| --- | --- |
| Full solution build | Passed |
| Full Engine suite, including DNG regressions | 2,527 passed |
| Full Web suite | 683 passed; 12 existing `knightly-boots` content-identity failures |
| JavaScript and browser-dispatcher security suites | 968 passed |
| Protected desktop/narrow Edge workflows | Passed; no console/request errors |

Edge inspection covered 50x50 River Valley, 120x100 Through Routes without Sites
and 120x120 Woodland. It exercised accepted nested operations, Before/After,
zoom, 600px and 390px layouts, repeat generation and a new seed. DELVE1 export
and Designer handoff were exercised while inspecting an intermediate stage; the
Designer result passed Engine validation. HTTP/client regressions additionally
assert that handoff and export use the accepted final terrain and Edges.

Observed single-frame DOM render times were 83–88 ms at 50x50, 538 ms at
120x100 and 569–623 ms at 120x120. These are individual Edge observations,
not browser microbenchmarks or retained DOM memory measurements. Large inspection
frames remain visibly slower than small ones. Three.js was not involved.
Browser evidence is recorded in `assets/previews/world-routes-verification.json`;
raw logs and captures remain under `.playwright-cli/browser-qa/`.

Reproduce from the repository root with restored dependencies:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --routes --fixtures .playwright-cli/routes-fixtures
python tools/render-routes-gallery.py .playwright-cli/routes-fixtures assets/previews
```

Resolve installed Node/Pillow-capable Python and set `PROJECTDELVE_PYTHON` as
documented in AGENTS.md. Browser checks use the playwright-cli skill and protected
dispatcher, disposable Edge contexts and a separate 5081 host. Preserve port 5080.
Known `knightly-boots` content-reference failures remain separate from this work.

Limitations: route profiles use tiled cardinal geometry and square-width corridors;
bounded retries do not guarantee a feasible route is found. Site attachments are
currently published with verified width 1; wider independent requests require
matching explicit access. Ordinary Open and Doors-openable facts are geometric,
not final scenario playability. Global routes, directed railway generation, race
tracks, canals, portals, population, Objectives and expanding worlds are deferred.
The known Three.js degradation near 80x80 is a separate concern and was not addressed.
