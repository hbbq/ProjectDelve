# Mixed Water Terrain V1

Mixed terrain uses ordinary typed terrain definitions. Wet Grass, Rocky Water and
Stepping Stones permit both land and water travel. Their categories do not decide
movement, LOS or excavation eligibility. Mountain shells still require Solid Rock.

Natural sampling uses the existing absolute-coordinate water shapes and a separate
seed domain, without changing recipe syntax or consuming construction RNG streams.
For intermediate Water coverage (1–99), grass/dirt directly beside sampled Water
has a 30% coordinate-hash chance of Wet Grass. Additional Wet Grass patches use
24×24 world owner squares: 20% of owners receive one circular patch with radius
[1.25, 2.25) Cells, clipped to grass/dirt. Rocky Water uses 16×16 owner squares with a 35%
owner chance and radius [1.25, 2.25) Cells, clipped to sampled Water. These are sparse
natural clusters; a cluster can span a narrow waterway, but no island connection is
promised. There is no per-world minimum of any mixed terrain. Zero and 100 Water
coverage retain their pure endpoint policies.

Patch centers, radii and owner selection are coordinate-derived. Sampling includes
a one-Cell water-feature halo for shoreline decisions, including signed coordinate
limits. Natural crops agree at overlapping coordinates. Crop-local enrichment and
construction remain separate operations and may depend on their finite window.
The pipeline supplies the selected content catalog to sampling. Missing Wet Grass
retains the original grass/dirt choice; missing Rocky Water retains Water. The
sampler captures availability once, so reduced catalogs keep the same crop contract
without importing canonical content into the supplied catalog.

Route construction prefers Stepping Stones over dry fill for actual Water cells,
when that definition is available in the supplied catalog. Reduced catalogs retain
the existing route surface fallback. Existing mixed crossings need no construction
allowance and retain their terrain. Newly constructed complete-width corridors
still count every changed Water Cell and longest Water run against the existing
limits, preparation costs, reservation permissions and protected Site geometry.
No final repair pass connects all islands. Site construction can replace mixed
Water with dry terrain only with explicit FillWater preparation permission and
charges the existing Water preparation cost.

Lake Country and River Valley permit limited crossings by default; the other
presets retain crossings disabled. The UI option **Allow limited crossings
(Stepping Stones)** permits crossings without guaranteeing one or connecting every
island. Authors can override it. Existing WLD1 recipes retain their explicit
WaterCrossings value (`U0`/`U1`); selecting a preset supplies its current default.
Route intensity, density, widths, Water-run limits and construction budgets are
unchanged. Sampling tuning intentionally changes historical seed output; the same
current recipe/content/seed remains deterministic, and DELVE1 stores a concrete map.

Terrain analysis retains its land graph and separately reports WaterTravelLabels
and WaterConnectivity, derived from water movement permission with the same
Machine and openable-Edge policy. Mixed cells can join both graphs; neither graph
proves 2×2 access. Semantic Water membership groups water-containing landscape
formations and informs shores, route preferences and dry-rock exclusions. Island
creation remains limited to exact open Water, preserving existing mixed terrain.

`MixedWaterGenerationTests` owns small fixtures covering overlapping sampling,
sparse output, dual connectivity, Walls, route construction limits and protected
cells. Existing Mountain Dungeon, Civilization, Population and Quick Play checks
remain required affected-project verification.

## First frequency tuning pass (2026-11-12)

The reproduced diagnostic corpus uses seeds 1, 2, 3, 5, 8 and 13 and unchanged
recipes at 20×16, 30×24, 50×50 and 80×60. The 172 explicit recipes retain their
encoded crossing flags, so preset defaults are measured separately in 60 worlds
(five presets × six seeds × 30×24/50×50; Population Off, Loot Empty).
Entries below are total Cells / worlds containing the Type, before → after.

| Cohort | Wet Grass | Rocky Water | Stepping Stones |
|---|---:|---:|---:|
| 172 unchanged recipes | 1,251 / 101 → 2,076 / 107 | 248 / 46 → 656 / 59 | 604 / 40 → 618 / 40 |
| 60 actual preset defaults | 179 / 27 → 308 / 29 | 73 / 11 → 195 / 16 | 0 / 0 → 136 / 13 |
| 30 small default worlds (30×24) | 21 / 8 → 32 / 9 | 19 / 3 → 25 / 3 | 0 / 0 → 20 / 3 |
| 30 large default worlds (50×50) | 158 / 19 → 276 / 20 | 54 / 8 → 170 / 13 | 0 / 0 → 116 / 10 |

Natural mixed coverage stays modest: final Wet Grass/Rocky Water occupy
0.731%/0.231% of the explicit corpus, with per-world maxima 3.125%/2.222%.
No old natural mixed Cell disappears during sampling. Existing Civilization Site
preparation removes 179 Wet Grass and 27 Rocky Water Cells after tuning; no
enrichment or route stage removes them, and no Stepping Stones are overwritten.
Small worlds can still lose all their sampled Wet Grass to Site preparation
(including Lake Country 30×24 seed 5 with crossings enabled).

Connectivity changes are not uniformly improvements. In the explicit corpus,
natural land/water component totals change 926/335 → 934/347 as added isolated
patches coexist with joins. Final component totals change 701/442 → 726/454;
summed largest land components decrease 147,660 → 147,131 while largest water
components increase 40,064 → 41,498. With actual defaults, final component totals
change 168/109 → 171/102 and largest land/water areas increase
52,723/9,812 → 53,369/10,024. These are 1×1 graph measurements, not guarantees of
island connections or larger-footprint access. Construction still reacts to the
changed geography within its existing rules. No further frequency adjustment was
justified by this corpus; six seeds and overlapping crops limit generalization.

The scratch reproducibility artifacts remain under `.tmp/mixed-water-frequency/`:
`tuning-before.jsonl`, `tuning-after.jsonl`, `tuning-repeat.jsonl`,
`tuning-summary.json`, `tuning_analyze.py` and `tuning-report.md`. All 172 baseline
fingerprints were reproduced before tuning; all 232 tuned worlds reproduced
exactly. Focused tests cover signed-coordinate crop seams, coordinate limits,
reduced catalogs, bounded crossings and preset overrides.
