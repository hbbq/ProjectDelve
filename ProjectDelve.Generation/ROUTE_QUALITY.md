# Route visual quality refinement

This pass retains the route generator, typed requests, shared PlanningBoard,
atomic patches, exact Site permissions and WindowLocal semantics. Natural Terrain
and DNG1/DNG2 are unchanged. Routes still use ordinary cardinal Cell and Edge
transitions. No new content, movement rules or rendering mechanics were added.

## Findings and changes

The saved galleries and server geometry identified several interacting causes:

- Flat Manhattan-cost terrain and turn penalties rewarded concentrating direction
  changes into a few large elbows. A scalar variation field alone did not give
  trails a consistent preference for gradual bends.
- Tree clearing cost 3 while Forest Trail variation ranged from 0 to 28. Broad
  low-cost field valleys could dominate actual natural openings. Explicit
  forest-edge, shore and rock-contact preferences were absent.
- Fresh corridors paid an extra cost while reuse discounted almost all variation.
  Combined with independently selected ports near corners, this encouraged long
  shared trunks and large L-shaped primary geometry.
- Boundary travel had no soft penalty. A boundary was as attractive as interior
  ground even when it was only an arbitrary crop of the world.
- Default Site ports were ranked by orientation and distance without first
  considering usable ground. The narrow fixture requested Water-facing ports
  despite having Grass/Tree-facing alternatives.
- Exploration considered disconnected regions before bypasses. It sampled only
  each route's middle, never compared separate routes for useful shortcuts, and
  treated a cut in the route graph as evidence of a physical bottleneck. Strong
  avoidance of the existing route could produce long parallel bypasses.

`RouteCostField` now supplies a soft, seed-derived, smoothly varying guide between
bounded endpoint sets. Forest Trails use a larger lateral envelope; Minor Paths
use a smaller one; Roads and Rock Passages have none. It neither imposes waypoints
nor bypasses terrain/Edge legality. Exact network chords can use a guide; large
network destination sets retain cost-aware attachment without an invented centre.

Terrain contacts favour forest edges/openings, usable shore contacts and rock
passes. Tree and rock preparation have more influence relative to variation.
Positive base costs are 12 for new ground and 8 for reuse, reducing excessive
reuse incentives. A soft boundary band and corner setbacks discourage arbitrary
edge-following. Adjacent fresh corridors have a small penalty, and Site/boundary
approach directions influence the first/last transition. These remain preferences;
obstacles and exact permissions still decide what is possible.

Automatic Site-port selection prefers passable ground, then clearable Trees,
before more difficult terrain. Explicit connection requests keep their exact
published ports. Building/room generation and access permissions are unchanged.

Exploration checks route detours and actual analysis chokepoints before region
branches. Bottleneck bypasses are local, and a claimed physical bypass must also
disconnect the ordinary walkable board when its selected cut is removed. Bounded
cross-route chords can shorten an otherwise indirect network in open terrain.
Accepted alternatives require useful distance savings, a genuine physical bypass
or new-region access; straight open roads still receive no artificial loop.
Candidate chord searches consume the shared planning budget.

Spatial variation is precomputed once per Cell rather than reevaluated for every
heading expansion. The minimum cost is 8, keeping the scaled A* heuristic
admissible. Search order consumes no random values. Surface painting remains
unchanged to keep routes readable and semantic identity straightforward.

## Evaluation

`tools/compare-route-quality.py` compares the saved baseline with refined server
fixtures. It asserts identical completed codes/windows and decoded original
Natural Terrain, then produces paired primary/final galleries and close-ups at
identical coordinates. Civilization placement may legitimately shift because
route preferences and reservations changed. The baseline images and performance
record are retained separately.

The diagnostics record straight-run distributions, turn frequency/angles,
length/Manhattan and length/Euclidean ratios, preparation, corridor reuse,
junction count/degree, Site intent outcomes, alternative usefulness and boundary
proximity. They are evaluation measurements, not acceptance quotas. All actual
Cell turns remain cardinal; improved gradual bends are visible at map scale.

See `assets/previews/world-routes-refinement-diagnostics.json` for all 20 fixture
comparisons and per-stage performance. Images are named
`world-routes-refinement-<fixture>-primary.png` and `-final.png`; four `detail-*`
images show junctions, attachments, bends and crossings. Baseline and refined
inputs use the same seeds and effective settings, including negative offsets.

| Preset | Primary routes | Final network |
| --- | --- | --- |
| Grassland | [comparison](../assets/previews/world-routes-refinement-120x100-grassland-primary.png) | [comparison](../assets/previews/world-routes-refinement-120x100-grassland-final.png) |
| Woodland | [comparison](../assets/previews/world-routes-refinement-120x100-woodland-primary.png) | [comparison](../assets/previews/world-routes-refinement-120x100-woodland-final.png) |
| Rocky Wilds | [comparison](../assets/previews/world-routes-refinement-120x100-rockywilds-primary.png) | [comparison](../assets/previews/world-routes-refinement-120x100-rockywilds-final.png) |
| Lake Country | [comparison](../assets/previews/world-routes-refinement-120x100-lakecountry-primary.png) | [comparison](../assets/previews/world-routes-refinement-120x100-lakecountry-final.png) |
| River Valley | [comparison](../assets/previews/world-routes-refinement-120x100-rivervalley-primary.png) | [comparison](../assets/previews/world-routes-refinement-120x100-rivervalley-final.png) |

For the fixed 120x100 cases:

| Preset / primary profile | Accepted primary routes | Longest straight run | Run p90 | Turns per 100 steps |
| --- | --- | --- | --- | --- |
| Grassland / Road | 2 → 2 | 109 → 92 | 109 → 92 | 1.3 → 2.1 |
| Woodland / Forest Trail | 2 → 2 | 37 → 24 | 24 → 8 | 12.0 → 27.9 |
| Rocky Wilds / Rock Passage | 1 → 2 | 30 → 73 | 27 → 44 | 9.0 → 5.6 |
| Lake Country / Minor Path | 0 → 2 | unavailable → 25 | unavailable → 10 | unavailable → 21.4 |
| River Valley / Minor Path | 2 → 2 | 60 → 33 | 24 → 13 | 7.8 → 17.4 |

This intentionally does not make every profile more winding. Rocky Wilds now has
two successful, relatively direct passages rather than one; Lake Country now has
successful primary routes. Woodland's primary length fell from 300 to 272 steps
while its bends became more distributed. Its final route preparation cost fell
from 594 to 549. Grassland's primary boundary fraction fell from 6.2% to 0%; its
additional useful links raise total construction cost from 485 to 666. All five
120x100 cases retain three accepted Site connections. The narrow case improves
from no accepted routes/attachments to a Through Route and two attached Sites;
placement and accepted Site counts can change with upstream route reservations.

Release performance uses the same benchmark protocol as the baseline: three
warmups, five samples per case, sequential generation with no concurrent tests
or browser work. The table averages the five preset medians at each size:

| Board | Generation, baseline → refined | Allocated, baseline → refined |
| --- | --- | --- |
| 50x50 | 159.7 → 159.6 ms | 64.6 → 66.8 MB |
| 120x100 | 353.4 → 330.8 ms | 211.6 → 214.8 MB |
| 120x120 | 433.7 → 406.6 ms | 254.6 → 254.7 MB |

These are whole-pipeline comparisons, not isolated A* microbenchmarks. Different
successful routes change downstream Civilization and validation work. Individual
cases vary: Grassland allocates more with additional useful alternatives; some
Water-heavy cases accept more primary routes. Per-stage times, allocations,
preparation, search work and counts are retained in the diagnostics JSON. Retained
managed preview memory stays below 1.5 MB in the five-preset samples. This is a GC
probe, excluding browser DOM memory. Times are local observations, not guarantees.

## Verification and limits

Focused regressions cover guide/profile differentiation and exact-endpoint
chords, boundary avoidance, bounded reuse detours, automatic port choice,
cross-route shortcuts and rejection of false physical-bypass claims. Existing
tests cover deterministic replay, widths, atomic rollback, reservations, exact
port identities, semantic metadata and preview/export/handoff.

Final verification on 2026-11-06: build passed; 56 focused generator tests passed;
2,533 full Engine tests passed; 683 Web tests passed with only the 12 established
`knightly-boots` content-identity failures; all 968 JavaScript/security tests
passed. Independent read-only reviews found no outstanding correctness blockers.

The protected Edge workflow checked a 120x100 Woodland case at desktop and 390px
width, accepted nested route inspection, Before/After, Generate Another, DELVE1
export while inspecting an intermediate frame and final Designer handoff. A new
accepted operation tree was awaited before checking the regenerated seed. Designer
reported `Valid · checked by the engine`; console/request error lists were empty.
Captures are under `.playwright-cli/browser-qa/inspection-jgzRId`. Browser render
times are observational only and were not used in the server performance table.
The temporary 5081 host was stopped; the existing 5080 host was preserved.

Reproduce with restored dependencies and the installed Node/Pillow runtimes
described in AGENTS.md:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --routes --fixtures .playwright-cli/routes-quality-refined
python tools/compare-route-quality.py .playwright-cli/routes-quality-baseline .playwright-cli/routes-quality-refined assets/previews
```

The baseline fixture directory was captured before changing routing. Paired PNGs
and the diagnostics JSON preserve comparison evidence independently of that local
cache. Full logs are `.playwright-cli/routes-quality-{build,focused,full-dotnet,full-node,benchmark}.log`.

Cardinal grids retain visible individual right angles. Roads and rock passages
can retain long straight segments. The soft guide has bounded amplitude and can
be overridden by terrain; it is not a spline or a new routing system. Optional
links may remain unresolved under Water, disturbance or planning limits. Some
extra accepted alternatives increase construction/validation allocations. No
universal connectivity, universal winding or numerical loop quota is enforced.

Further work could tune a broader seed corpus, improve junction surfacing and
study curved macro-guides for roads where justified. Global routes, new traversal
types, elevation and Three.js performance remain outside this pass.
