# WLD1 Site Population A2

A2 redistributes existing Population allowances into selected Buildings and dungeon
rooms. It adds no gameplay mechanic, runtime Site state, new generation option or
recipe field. The same current WLD1 recipe/content/seed reproduce deterministically;
historical WLD1 output equivalence is not required. A1's uncommitted implementation
was captured as the baseline before these changes; the comparison is not against
the older Git HEAD.

## Selection and vacancy

An **active** structure is selected for monster occupation, even if restrictions
prevent some or all requested placements. **Monster-free** means no generated
occupants in the structure; **completely empty** additionally means no meaningful
Loot, Machine, Resource or known ground object. Furniture and terrain do not count
as rewards. Diagnostics report these outcomes independently.

Initial large thresholds follow the gameplay-quality investigation: at least 50
usable room Cells for Buildings and 100 usable room/corridor Cells for dungeons.
Large structures use their own 10% monster-vacancy draw. Large Buildings no longer
multiply a settlement vacancy gate by a building gate. Smaller structures retain
ordinary Site/building vacancy and small compound opportunities.

The choice is independent of A1's unchanged Site Loot selection. Before placement
restrictions and optional supply settings, the nominal joint vacancy is roughly
3.6% for a large Building (10% monster vacancy times 36% A1 reward vacancy), and
2% for a large dungeon (10% times 20%). This is a policy expectation, not an
all-seeds guarantee. Empty Loot, Population Off, supplies-only regions, legal
footprints and scarce global budgets can produce more empty locations. No rule
forces a reward into a monster-free location.

The investigation found 65% empty rooms in large Buildings and declining Unit
density as structures grew. A1 reduced unrewarding exploration but left monster
selection largely independent of area and density. The lower large-structure
vacancy probability and bounded regional allocation address that specific gap,
while preserving explicit vacancy and surprise.

## Bounded room allocation

`SitePopulationAllocation.Select` consumes existing typed `SitePopulationIntent`,
`SiteEncounterRegion`, Building and room metadata. It also retains typed encounter
regions on non-dungeon Sites; there is no separate content execution system.

Room requests vary with area, density and seeded jitter. At most four eligible
regions are selected per structure. A selected large structure with at least two
eligible regions normally requests at least two, subject to fitting their authored
minimums within the Site allowance. A farthest-apart room ordering spreads
requests instead of concentrating them at one entrance. For three or more eligible
regions, a seeded 75% choice leaves at least one intentionally quiet room; otherwise
all eligible rooms may be selected. Tiny or constrained regions can still fail.

The initial region-count target is `ceil(area / divisor) + floor((rooms - 1) / 3)`
plus zero or one, clamped to the limits above:

| Structure | Low divisor | Medium divisor | High divisor |
| --- | ---: | ---: | ---: |
| Building | 45 | 35 | 25 |
| Dungeon | 120 | 80 | 55 |

The Unit target is `ceil(area / Unit divisor) + selected regions` plus zero or
one. The Site ceilings below apply before world limits and authored region maxima:

| Structure | Low divisor / ceiling | Medium divisor / ceiling | High divisor / ceiling |
| --- | ---: | ---: | ---: |
| Building | 18 / 6 | 13 / 9 | 9 / 12 |
| Dungeon | 35 / 12 | 24 / 18 | 16 / 24 |

Small Buildings remain capped at five Units, preserving their previous upper
range. Regional quotas start at their declared minimums, then distribute remaining
allowance across selected regions up to their maxima (generated building regions
cap at six; native dungeon regions retain their existing room-size caps). A request
whose minimum cannot fit the bounded Site allowance is omitted rather than silently
lowering its authored minimum. Empty regions cannot create invalid anchor queries.

Requests are interleaved across Sites before a Site's extra requests. They consume
the existing world limits, ahead of ordinary wilderness requests. Global targets,
density-dependent Unit allowances, the 256-Unit/192-Group/160-encounter hard ceilings,
1,024 attempts and two-million-work bound are unchanged. This redistributes
occupation; it does not raise the world population controls.

### Buildings and settlement roles

Building room regions use existing building defender compositions, Dormant resting
roles and ordinary Guard Area Groups. They retain complete-footprint and portal
restrictions. Furniture, protected access and existing Loot Cells remain clear.
Supply-role Cells are excluded from the new independent room occupation regions.

Settlements with a large Building use independent room allocation instead of
allowing a small courtyard compound quota to stand in for the large interior.
Small-building settlements retain existing connected compound occupation and role
preferences. A compound owns its shared request once; `CompoundMember` building
rows show actual interior occupants without claiming separately requested quotas.
Unregional structured camps retain their ordinary scheduling and behavior.

### Dungeons and Alert

Dungeon demand includes the complete usable exploration footprint, including
absorbed natural passages, but occupants use the existing authored room regions.
Corridors remain quiet. Native composition preferences, optional sentries/local
patrols and rare existing large guards remain available. Room-bound large Units
still require complete valid footprints and are not promised passage through the
whole dungeon.

Every accepted room encounter must form its intended connected Alert component
and remain separate from other encounters in the complete initial friendly graph.
Walls, Doors and Group identities do not isolate Alert. Placement can therefore
reduce or reject a requested encounter. There is no geometry mutation, automatic
bridge, new wake rule or fallback that bypasses Alert or legal placement.

## A1 and diagnostics

A1 deck selection, Sparse/Standard/Plentiful formulas, dynamic equally represented
All membership, finite preset provisioning, draw-on-pickup and defeat-drop rules
are unchanged. Site rewards still run after Population and independently select
locations, including monster-free ones. New occupants can change which reward
Cells are free, and changed accepted encounter totals can naturally change A1's
encounter-related marker counts. They do not change the selected abundance or mix.

`PopulationStatistics.UnitAllowance` is separate from `RequestedUnits`, which now
counts actual attempted requests, including failed attempts. The latter is not a
promise that every request fits or that every allowance is used. Per-structure
`SiteStatistics` report selection/vacancy, intentionally omitted rooms, requested
encounters/Units, accepted occupation, placement reductions, final Alert rejection,
budget/work reductions and omissions. Noncombat counts are finalized after Site
Loot. `PlanningBudget` records refused work spends for diagnostic attribution;
it does not change allowed work or placement behavior.

These facts appear only in the authoring preview, with a bounded list. They are
not included in scenario gameplay, Fog of War observations, saves or replay state.

## Paired corpus

`tools/WorldGenerationBenchmark/SitePopulationInvestigation.cs` measures 48 complete
recipes: Grassland/RockyWilds; 40x35/100x80; seeds 1, 12, 12345, plus RockyWilds
100x80 seeds 2..5; Low/Medium/High population; Difficulty 5; All/Standard Loot;
offset (-17,-13); native preset terrain/enrichment/civilization/routes.

Both A1 and A2 runs had zero generation errors, all Engine/PlayReady validations
passed, and recomputed complete Alert graphs matched the published diagnostics.
Four recipes per run were regenerated deterministically. All 48 paired geography
hashes and Loot card mixes matched; every All deck retained equal copy counts and
sufficient finite supply for its markers. Before/after JSON includes every recipe,
structure and room rather than only averages.

Large structures in the 100x80 subcorpus (five Buildings and eleven dungeons per
density) changed as follows. Counts repeat geometry across density settings and
must not be treated as independent geographic samples.

| Structure | Density | Units before → after | Units / 100 Cells before → after | Monster-free rooms before → after | Completely empty before → after |
| --- | --- | ---: | ---: | ---: | ---: |
| Building 50+ | Low | 6 → 28 | 1.86 → 8.70 | 10/13 → 3/13 | 1 → 0 |
| Building 50+ | Medium | 6 → 38 | 1.86 → 11.80 | 10/13 → 1/13 | 1 → 0 |
| Building 50+ | High | 6 → 47 | 1.86 → 14.60 | 10/13 → 0/13 | 1 → 0 |
| Dungeon 100+ | Low | 53 → 74 | 3.62 → 5.05 | 20/44 → 15/44 | 0 → 0 |
| Dungeon 100+ | Medium | 53 → 95 | 3.62 → 6.48 | 20/44 → 9/44 | 0 → 0 |
| Dungeon 100+ | High | 54 → 106 | 3.69 → 7.24 | 20/44 → 9/44 | 0 → 0 |

Meaningfully empty room percentages for large Buildings fell from 23.08% to
7.69%/7.69%/0%; large dungeons remained at 0% because A1 already rewarded their
quiet rooms. The previously problematic RockyWilds seed 1 High dungeons, of 111
and 103 usable Cells, changed from 0/2 Units to 6/8. No active large structure in
the corpus retained the investigation flag of fewer than four Units or fewer than
two Units per 100 Cells. The separate deterministic fixture tests still demonstrate
intentional large vacancy, both rewarded and genuinely empty.

Across all sixteen worlds per density, global totals barely changed:

| Density | Units before → after | Encounters before → after |
| --- | ---: | ---: |
| Low | 1,236 → 1,217 | 395 → 396 |
| Medium | 2,146 → 2,144 | 761 → 781 |
| High | 2,918 → 2,926 | 1,087 → 1,143 |

### Remaining small-structure tail

The improvement is deliberately not a universal occupancy floor. In the 100x80
subcorpus, the 116 small Buildings per density became quieter:

| Density | Occupied before → after | Rewarded monster-free before → after | Completely empty before → after | Meaningfully empty rooms before → after |
| --- | ---: | ---: | ---: | ---: |
| Low | 50 → 22 | 42 → 54 | 24 → 40 | 29.82% → 39.77% |
| Medium | 51 → 22 | 39 → 54 | 26 → 40 | 30.41% → 39.18% |
| High | 51 → 22 | 41 → 54 | 24 → 40 | 29.24% → 40.35% |

Their Units per 100 Cells changed from 5.60 to 2.27/2.50/2.63. Storage-only rooms
remain monster-free; denser room occupation also competes for nearby legal
Alert-separated space. There were 135 rejected Site placements in the after corpus,
zero final Alert-topology rejections, three global-budget omissions and no work
omissions. The shared failed-footprint message mentions Alert spacing, so counting
messages containing "Alert" is not a count of invalid Alert networks. The complete
graph checks remained valid throughout. This tail is a playtest tradeoff, not hidden
by filling supplies rooms or coupling Loot to monster occupation.

For the six 40x35 worlds per density, small-Building complete vacancy changed from
7/4/5 to 11/10/11; meaningful empty rooms from 29.79%/21.28%/23.40% to
34.04%/31.91%/31.91%. Their independent A1 rewards still provided 19/20/19
monster-free rewarded Buildings. Both map sizes and all raw per-room observations
remain in the corpus artifacts.

## Reproduction and verification

```powershell
dotnet run --project tools/WorldGenerationBenchmark --artifacts-path .world-benchmark -- --site-population .playwright-cli/a2-population-current.json
```

Captured results are `.playwright-cli/a2-population-before.json`,
`a2-population-after.json` and `a2-population-final-summary.txt`. The isolated
baseline/candidate assemblies and replay instructions are retained in
`.playwright-cli/a2-baseline`, `a2-after` and `a2-harness/README.md`. The baseline
was compiled before A2 changes; it is not reconstructed with a second implementation
of the old selection algorithm.

Focused test-owned fixtures cover area/room/density scaling, declared minimums,
generic typed regions, quiet rooms, intentional vacancy, monster-free rewards,
supplies/furniture/access and preexisting Loot, budget omission accounting,
deterministic generation and PlayReady validity. Existing A1 abundance and deck
tests remain in place. Full verification passed 3,674 Engine tests and 804 Web
tests; client/security suites and 56 focused generator-client tests passed.

Playtesting should evaluate room-by-room combat pacing, the strong High-density
large-building concentrations, small storage/neighboring-building quietness, and
whether room-bound large guards count as sufficient encounters. No strict power
balance, universal survival guarantee or content guarantee per room is established.
