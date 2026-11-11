# Density Scaling V1

WLD1 uses `ContentDensity` (`None`, `Low`, `Medium`, `High`, `VeryHigh`)
for Campsites, Settlements, Through routes and Exploration links. These are
abundance targets, independent of Civilization preparation intensity, route
intensity, Population density and Difficulty. Defaults are Low Campsites,
Medium Settlements, Low Through routes and Low Exploration links.

Let `A = width * height`, `L = 0..4` in enum order, and
`E = floor(sqrt(A * 720))`. None always requests zero. Otherwise:

| Content | Target | Hard target cap |
| --- | --- | --- |
| Campsites | ceil(E * L / 720) | 32 |
| Settlements | ceil(E * L / 1440) | 24 |
| Mountain Dungeons | ceil(E * max(CampsiteLevel, SettlementLevel) / 2400) | 4 |
| Through routes | ceil(sqrt(A) * L / 30) | 12 |
| Exploration links | ceil(E * L / 3600) | 16 |

Caps apply after rounding. Dimension growth fits large structural footprints
and boundary-spanning routes better than direct area growth. Density does not
enlarge each Site or route. Very small windows can request a Site that cannot
fit; the existing bounded planner reports its rejection rather than publishing
partial geometry. Targets do not promise successful placements.

| Window | Camps Low / Medium / High / VeryHigh | Settlements | Through | Links |
| --- | --- | --- | --- | --- |
| 30x24 | 1 / 2 / 3 / 4 | 1 / 1 / 2 / 2 | 1 / 2 / 3 / 4 | 1 / 1 / 1 / 1 |
| 60x50 | 3 / 5 / 7 / 9 | 2 / 3 / 4 / 5 | 2 / 4 / 6 / 8 | 1 / 1 / 2 / 2 |
| 80x80 | 3 / 6 / 9 / 12 | 2 / 3 / 5 / 6 | 3 / 6 / 8 / 11 | 1 / 2 / 2 / 3 |
| 120x120 | 5 / 9 / 14 / 18 | 3 / 5 / 7 / 9 | 4 / 8 / 12 / 12 | 1 / 2 / 3 / 4 |

Targets are seed invariant; placements, footprint requirements, family choices
and endpoint locations use existing deterministic seed streams. Request keys
remain ordinal, so increasing abundance does not reseed earlier requests.
Mountain Dungeons precede Settlements and Campsites and have separate targets;
there is no new density control. Both Site densities None, or disabled Civilization,
requests zero dungeons. Counts are exposed in `civilizationStats.dungeonsRequested`,
`dungeonsAccepted` and `dungeonsOmitted`. See [Mountain Dungeons V1](MOUNTAIN_DUNGEONS_V1.md).
Settlements are still planned before Campsites. Natural terrain and Population
selection are separate stages. Settlement families, route access and connectivity,
protected areas, preparation allowances and physical geometry are unchanged.

Capacity is determined by the existing transactional planners against remaining
geometry, reservations, preparation allowances and shared budgets. Civilization
retains 512 attempts and 2,000,000 work units; each route stage retains 128
attempts and 2,000,000 work units. These are not increased with density or size.
Exploration links require existing network geometry and must demonstrate useful
alternative access or region connection. Their candidate search and quality gate
remain bounded; shortfalls are expected where useful opportunities are absent.
Route profiles determine width; intensity continues to govern preparation and disturbance limits.

Preview frames expose `campsiteDensity`, `settlementDensity`,
`throughRouteDensity` and `explorationLinkDensity`. Each allocation records
`requested`, `attempted`, `accepted` and `omitted = requested - accepted`.
Exploration shortfalls include an allocation `reason`, including when no useful
candidate exists and therefore no route intent was attempted.
Attempted means top-level Site requests or route candidates submitted, not nested
planner retries. Exploration can attempt more candidates than its accepted-link
target; rejected alternatives retain their intent and reason. Absent/disabled
stages have no allocation. Planning failures and route intents retain detailed
rejection reasons; work and nested retry counts remain in existing statistics.

The ordered WLD1 keys remain N/V/T/A but values are names, for example
`-C1-NLow-VMedium-P2-Q1-TLow-JMinorPath-L1-ALow-U0-Z2`.
Old numerical density fields are rejected; no migration or historical WLD1
compatibility is provided. DELVE1 continues to preserve exact concrete scenarios.

Run the JSON-lines calibration sweep sequentially:

```powershell
dotnet run --project tools/WorldGenerationBenchmark -c Release --artifacts-path .world-benchmark -- --density --sizes=30x24,60x50,80x80,120x120 --seeds=42,991 --levels=Low,VeryHigh --presets=Grassland,Woodland,RockyWilds
```

Omit overrides for all five levels. Records include all four allocations,
settlement families, occupied quadrants and nearest-site distance, construction
and routing work, components, per-stage and total time. Timings are single runs
for corpus calibration, not warm median performance measurements. Existing
benchmark modes retain their warm performance investigation workflows.


## Calibration corpus

The final-policy Debug sweep on 2026-11-11 uses seeds 42 and 991 and the four
required sizes. It contains 120 cases: simultaneous Low/VeryHigh across Grassland,
Woodland and RockyWilds (48), None across all sizes in Grassland (8), independent
Sites across all sizes and four nonzero levels in Grassland (32), and independent
Through and Exploration across 30x24/120x120 and four levels in Grassland
(16 each). The earlier exploration divisor was discarded after independent
measurement showed that even Low saturated useful candidates. The final divisor
3600 requests 1/2/3/4 links on the largest board so abundance can affect results
before candidate quality and capacity limits apply.

The benchmark supports `--mode=Sites`, `--mode=Through` and `--mode=Exploration`
to vary one category while holding the others at Low (`--mode=All` is the default).
Sites varies Campsites and Settlements together. Larger settlement requests take
priority over Campsites and can consume their available space; raising both
settings therefore promises monotonic requests, not monotonic accepted Campsites.
Extra protected primary routes can likewise consume Site capacity when raising
all four settings together. Missing useful alternatives are explicit Exploration
omissions, including when no candidate is submitted.

The compact [raw JSON-lines corpus](../assets/previews/density-scaling-v1-calibration.jsonl)
contains requested/attempted/accepted/omitted allocations, distributions,
connectivity, family, work and phase timing records. Mode distinguishes the
independent sweeps from simultaneous changes. Every record uses the final
formulas above; no obsolete-policy measurements are retained in this corpus.

Corpus totals (these aggregate repeated configurations across sweep modes):

| Content | Requested | Attempted | Accepted | Omitted |
| --- | --- | --- | --- | --- |
| Campsites | 646 | 646 | 484 | 162 |
| Settlements | 364 | 364 | 359 | 5 |
| Through routes | 482 | 482 | 433 | 49 |
| Exploration links | 160 | 181 | 82 | 78 |

Independent Sites acceptance ranges over both seeds, routes held at Low:

| Window | Sites Low | Medium | High | VeryHigh |
| --- | --- | --- | --- | --- |
| 30x24 | 1 | 1 | 1-2 | 1-2 |
| 60x50 | 5 | 7 | 6-10 | 6-12 |
| 80x80 | 5 | 9 | 12-14 | 13-18 |
| 120x120 | 8 | 14 | 21 | 26-27 |

Independent Through requests accepted 1/2/3/4 routes respectively on 30x24.
On 120x120 they accepted 4, 7-8, 11-12, 11-12; the upper two levels reach
the deliberate target cap. Independent Exploration on 120x120 accepted 1,
2, 2-3, 2-3 links respectively, demonstrating that abundance affects accepted
links before the useful-opportunity limit. On 30x24 no useful alternatives
existed, so every level correctly reported omissions rather than artificial loops.
All independent sweeps retained exactly one route-network component. None
produced no Sites or routes at every tested size.

All four settlement families occurred. Occupied quadrants ranged 1-4 for
nonempty maps; mean nearest Site distance ranged 0-34.5 Cells (zero denotes a
lone Site). Civilization peaked at 565,992 charged work units and 372 nested
attempts, below the unchanged 2,000,000/512 limits. Combined routing work over
all route stages peaked at 1,230,013; each stage retains its existing budget.
Maximum observed total generation time was about 6.21 seconds in Debug.
These are environment-specific single-run calibration timings, not controlled
warm performance comparisons; per-stage timings and counts are retained in the
raw corpus. No resource caps, preparation rules or useful-alternative quality
checks were relaxed to obtain these results.
