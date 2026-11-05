# World Generator

World Generator is an authoring pipeline separate from DNG1-HVM and DNG2-OS.
Open **World Generator** in the browser, choose a preset or explicit settings,
and generate terrain. The result contains no Units, Objectives, Loot or FoW.
It can be exported as DELVE1 or opened in Scenario Designer for further authoring.

## World and window

`WorldDefinition` contains the completed root seed and Natural Terrain settings;
its algorithm version is WLD1. `WorldWindow` contains signed OffsetX/OffsetY and
positive Width/Height. Bounds are half-open. Every cell samples
`(OffsetX + LocalX, OffsetY + LocalY)`, independently of window size, traversal
order or previous requests. The entire requested window must fit signed 32-bit
world-cell coordinates. Negative lattice coordinates use mathematical floor.

Web authoring and detached generation support windows up to 120 by 120, including
120 by 100 and transposed rectangles. Hosts can lower the authoring ceiling with
`Authoring:MaxBoardSize` (1..120; default 120). This is a resource policy, not a
game rule. DNG1/DNG2 retain their separate maximum of 50 in each dimension.
The pure sampler can sample any supported world position without generating
preceding windows. See [Large map support](LARGE_MAP_SUPPORT.md) for measured
limits, payload budgets, verification and remaining costs.

## Recipes and controls

```
WLD1-30x24-X-25-Y-12-F25-W15-R10-KBalanced-HRiver-E1-I2-B1111-S0000000000003039
```

This selects a 30 by 24 rectangle starting at (-25, -12), with forest/water/rock
amounts 25/15/10, Balanced scale, River water form, and root seed 12345 (hex 3039).
Fields are strict and ordered. `K` accepts Fine, Balanced or Broad. `H` accepts
Auto, Lakes, River or Mixed. Coverage values are integers from zero to 100 and
must total at most 100. Coverage controls approximate landscape-wide density;
a particular crop may have more, less or none of a requested terrain.

The optional enrichment suffix is `-E0/1-I1/2/3-Bffff`: E enables enrichment,
I is low/medium/high intensity, and the four B bits enable Forest, Rock, Water
and Grassland respectively. For example `-E1-I2-B1010` enables Forest and Water.
All effective values are encoded, independent of presets. Omitting the suffix
selects Natural Terrain only. The World Generator remains WLD1 under the current
development contract; DNG1/DNG2 recipe interpretation and deterministic outputs
are unchanged. Preserve concrete maps with DELVE1.

Omitting `-S` creates a seed once and returns a completed recipe. The browser
retains that seed when changing window offsets. Generate Another selects a new
seed; clearing the seed also requests a new world. Completed recipes include
effective values rather than preset identities. Preset tuning cannot reinterpret
an existing recipe.

Presets are Grassland, Woodland, Lake Country, River Valley and Rocky Wilds.
Loading a recipe changes controls without regenerating. Editing controls retains
the last preview and marks it outdated until another preview is requested.
Preview zoom offers Fit window or 12/24/48 pixels per Cell with scrolling, without
regenerating terrain or rebuilding the preview. Designer has its own board zoom.

Preview runs the ordered recipe immediately and retains bounded stage frames.
Select a stage or use Previous/Next; Before shows its preceding frame. Optional
overlays show changed Cells, original formation boundaries, current walkable
components/chokepoints, entrances and opportunities. Hover or click a Cell for
details. Export and Designer always use the final result, regardless of the
inspection frame. See [Terrain analysis and enrichment](TERRAIN_ENRICHMENT.md)
for architecture, evaluation, performance and limitations.

## Sampling and composition

Natural Terrain is a GlobalSeamless layer implemented as a stateless sampler.
Independent, versioned hash domains produce forest, rock, openness, soil and
water randomness. Smooth multi-scale value fields create correlated patches,
forest belts, clearings and rock formations. No window histogram, quota, border
painting, connectivity repair or map-local random sequence affects sampling.

Water uses stable feature owners on a world-space discovery grid. Each owner
defines an irregular lake or finite winding ribbon with independently hashed
placement, orientation and shape. Geometry extends beyond its owner square.
Discovery includes neighbouring owners outside the requested rectangle using a
conservative bounded reach; features are evaluated before cropping. Low water
amounts reduce the number of features instead of shrinking rivers into disconnected
sub-cell dots. Water features combine by union. Precedence is Water, Solid Rock,
Tree, then Dirt/Grass. Zero forest/water/rock leaves Grass and natural Dirt patches.

`WorldGenerationContext` samples a window into mutable output samples and retains
the field values. Materialization produces a Grass-default sparse typed board.
Changing that output cannot mutate the world definition or sampler. The generator
validates through ordinary Engine scenario creation with the selected content
provider. Canonical, Workspace and Scenario-local content retain their established
resolution and export boundaries. The Web fingerprint hashes the exact captured
scenario JSON, including embeddable Workspace definitions.

`WorldGenerationPlan` executes explicit typed steps with stable IDs and validated
dependencies. The default sequence is Natural Terrain, Terrain Analysis, enabled
Forest/Rock/Water/Grassland steps, then Post-Enrichment Analysis. Natural Terrain
remains GlobalSeamless; enrichment is WindowLocal and may differ between crops.
Original samples/formations remain available as provenance. Timings and allocation
measurements are separate from reproducible diagnostics. There is no plugin
registry, external recipe DSL, streaming or persistent world runtime.

## Dirt and appearance

Canonical `dirt` is passable, does not block LOS, allows ordinary Resource markers,
and has no production, categories or special effects. It has no legacy enum value.
The three existing opaque 1254 by 1254 PNG originals export through
`tools/prepare-artwork.py` to 256 by 256 sRGB WebPs:

| Source under assets/source/terrain | Runtime under wwwroot/art/terrain | Bytes |
| --- | --- | ---: |
| dirt-01.png | dirt/01.webp | 18,972 |
| dirt-02.png | dirt/02.webp | 19,034 |
| dirt-03.png | dirt/03.webp | 21,192 |

The shared Dirt recipe uses the existing terrain variant and quarter-turn
functions unchanged. World previews pass a world-stable appearance seed and
absolute coordinates, including for Tree artwork. Classic and Designer use a
distinct Dirt material; DOM and Three.js use the shared artwork recipe.

World artwork continuity is guaranteed in World previews. Designer draft provenance
retains the completed recipe and original definition. DELVE1 preserves concrete
terrain and embedded content, but carries no new world-appearance metadata.
Ordinary imported/test scenarios keep their established board-scoped appearance
seed and may use different texture variants or rotations. No running snapshot
format or ordinary-scenario appearance lifecycle changed.

## Original Natural Terrain visual gallery

[Ten fixed-seed Edge previews](../assets/previews/world-generator-v1.png) cover
all five presets and all three scales. The [recipe/coverage manifest](../assets/previews/world-generator-v1.json)
reproduces each map. [Adjacent windows](../assets/previews/world-generator-v1-seam.png)
show the same world at X=-25 and X=25, Y=-25, size 50 by 50, seed 12345,
F35/W25/R15, Broad/Mixed. A third overlapping window at X=0 was also inspected.

The gallery shows open grassland, clustered forest interiors and clearings,
large rock barriers, irregular lakes, and narrow/broad winding waterways.
Broad scale creates formations extending beyond individual previews. Fine scale
produces more local variation. Dirt forms exposed patches rather than independent
random cells. No feature-owner rectangular seams were visible. Local coverage
varies substantially in Broad windows, as intended. Rivers are finite landscape
features; elevation, drainage, tributaries and global hydrology are not modeled.

Desktop and 390px mobile Edge checks exercised preview, retained settings, recipe
copying, Classic/Enhanced presentation, DELVE1 export, Designer handoff and Engine
validation. A detached test scenario also checked Tabletop, Three.js Flat,
Enhanced DOM and Classic rendering. Successful inspections reported no console
errors or failed requests. The original port-5080 game was preserved.

## Original V1 investigation performance

Detached .NET 10.0.12 Release measurements use three warmups and the median of
seven samples. Root seed 123456789ABCDEF0, offset (-17, -13), default Natural
Terrain settings; no live game is advanced. Total generation includes sampling,
scenario projection, Engine validation and fingerprint serialization. Separate
Engine materialization is an additional measurement, not an additive phase.

| Size | Sampling ms | Total generation ms / allocation | Board projection ms / allocation | Engine materialization ms / allocation | Scenario JSON bytes | DELVE1 chars |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 30x24 | 0.53 | 2.48 / 1,163,288 B | 0.021 / 21,504 B | 1.43 / 918,344 B | 29,999 | 1,358 |
| 50x50 | 1.95 | 6.91 / 3,168,568 B | 0.048 / 143,920 B | 2.73 / 1,920,040 B | 195,228 | 7,133 |
| 120x100 | 6.23 | 34.84 / 13,211,320 B | 0.201 / 720,496 B | 15.22 / 6,818,320 B | 1,041,102 | 36,863 |

Actual Edge preview DOM construction measured 15.2 ms for a 30x24 desktop map,
13.3 ms for a 30x24 mobile map, and approximately 32-50 ms across the displayed
50x50 gallery examples. These browser figures exclude image decode, paint and
network latency; screenshots separately establish the final appearance.

The primary large-window costs remain Engine materialization/validation and
serialization, rather than pure field sampling or sparse board projection.
These were the measurements before enabling large-map authoring. The subsequent
[large-map investigation and implementation](LARGE_MAP_SUPPORT.md) profiles those
paths, indexes appearance and Designer render traversals, and avoids full-board
reconstruction for painting and ordinary selection. Runtime terrain lists and
LOS scans remain potential future optimization targets.

Reproduce the detached measurements from the repository root:

```powershell
dotnet run --project tools/WorldGenerationBenchmark -c Release --artifacts-path .playwright-cli
```

## Original Natural Terrain milestone verification

Focused tests cover recipes, fixed output, partitions/overlaps, negative and
extreme signed coordinates, dense feature discovery, sparse river connectivity,
precedence, zero/dense coverage, context isolation, Dirt gameplay, artwork sampling,
DELVE1, Workspace snapshots and Designer handoff. Existing DNG1/DNG2 regression
fingerprints are unchanged.

Final verification on this implementation:

| Check | Result |
| --- | --- |
| Solution build, cached dependencies | Passed; zero errors |
| Focused World/Dirt Engine tests | 34 passed |
| Focused World HTTP integration tests | 3 passed |
| Full Engine suite | 2,379 passed |
| Full Web suite | 664 passed; 12 pre-existing content-reference failures |
| Full JavaScript and dispatcher security suites | 946 passed |
| Desktop/mobile and four renderer Edge checks | Passed; no console/request errors |
| Existing DNG1/DNG2 source and CONSTRAINTS.md | Unchanged |
| Source artwork integrity | All 338 initial source-file hashes unchanged |

The 12 Web failures are all `ScenarioContentReferenceTests` conflicts for Equipment
`knightly-boots`. An isolated source snapshot of HEAD
`8b699d81543ee4fc417c6d21db3f692706a6fc9a` reproduced the same 12 failures
(4 other tests in that class passed), establishing that they predate this work.
No content or unrelated tests were changed to suppress them. Cached build/restore
also emitted NU1900 because NuGet vulnerability data was unavailable; compilation
and dependency resolution succeeded.

Commands used from the repository root:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
```

The installed Node executable was resolved directly because the sandbox nvm shim
was inactive. Model preparation tests used the installed Pillow-capable Python
via `PROJECTDELVE_PYTHON`, as documented in AGENTS.md. Edge inspection used only
the protected dispatcher and a disposable port-5081 host, which was stopped after
inspection. A fresh isolated host using the final build was then left on port 5081
for review; it has its own Workspace and leaves the original port-5080 game intact.
Remaining work is separate: resolve the pre-existing content-reference
collision, profile large-board authoring/rendering before raising host limits, and
introduce new world algorithm versions if future visual tuning changes output.
