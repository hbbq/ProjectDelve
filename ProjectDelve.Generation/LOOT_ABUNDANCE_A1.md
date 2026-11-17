# WLD1 Loot Abundance A1

This page records the A1 implementation and baseline measurements. Subsequent
[Site Population A2](SITE_POPULATION_A2.md) changes monster allocation independently;
the deck, abundance, Site reward and finite-supply semantics below remain in force.

WLD1 defaults to **All** and **Standard**. Complete recipes emit
`-O<preset>-LA<Sparse|Standard|Plentiful>-HC<capacity>` before the optional seed.
Omitted deck/abundance fields mean All/Standard under the current implementation.
Historical WLD1 layouts are not preserved. DELVE1 captures the concrete result.

All continues through `LootPresets.Resolve`, with one equally represented copy
of each effective Loot Card in ordinal Type ID order before uniform multiplication.
There is no second All implementation, rarity classification or quality setting.
Classic and other restricted compositions are unchanged. Empty suppresses both
encounter markers and Site rewards. These defaults apply to WLD1, not every
manually authored scenario.

## Quantity policy and rationale

The gameplay-quality investigation measured average initial marker counts of
6.47/12.38/18.93 at Low/Medium/High population. Most markers were outside Sites,
40.3% of buildings had no Unit or interactive content, and large buildings had
65% empty rooms. Those measurements justify separating exploration rewards from
monster count; they do not establish item-power balance or healing sufficiency.

Standard retains the encounter budget `min(48, max(1, acceptedEncounters / 3))`.
Sparse uses half rounded up; Plentiful doubles it, up to 96. Existing legal-cell,
occupancy and work limits can cause a reported shortfall. Population Off produces
no encounter markers. Ordinary deployment access replaces the obsolete extra
largest-component filter in complete WLD1 worlds.

Site rewards have a separate Standard global ceiling of 64; Sparse/Plentiful
ceilings are 32/128. Each selected building, dungeon or other suitable Site
location receives a Standard target of `ceil(usableArea / 24)`, clamped to 1..8.
Sparse halves that target rounded up; Plentiful doubles it to at most 16.
The initial generated total is therefore bounded by 224 markers. Small locations
can have equal Sparse and Standard counts because rewards are whole markers.

Every Site has an independent one-in-five reward-vacancy choice; individual
buildings have a further one-in-five choice. Neither abundance nor monster
occupation changes these choices. Entire Sites and individual rooms can remain
unrewarding. One marker per roughly 24 usable Cells is an initial exploration
policy, not a reward-per-room guarantee or power-balancing claim.

## Placement and finite supply

`SiteLootGenerator` runs after final geometry and Population, including when
Population is absent or Off. It uses ordinary Loot markers on reachable legal
unoccupied Cells. Deployment, entrances and required Site access paths remain
clear; terrain, Edges, monster selection and defeat probabilities are unchanged.

Seeded per-location streams order candidates by distance inward from entrances,
with random ties. Room candidates are interleaved before revisiting a room;
storage/supply metadata breaks equal-depth region ties. Dungeon corridor regions
also participate, allowing rewards in deeper branches. Locations are interleaved
through Sparse, Standard and Plentiful allocation tiers before applying global
ceilings. Population markers already placed on a candidate are respected.

No card identity is assigned to a marker. Existing `PopulationLoot.Provision`
repeats complete preset copies for all initial markers and expected initial defeat
drops. Custom supply is never expanded; every existing marker, including stacked
markers, reserves a card. Runtime exhaustion and draw-on-pickup are unchanged.
Site and Population requested/placed diagnostics expose optional shortfalls.
Final interactive registration and PlayReady validation run after Site allocation.

Quick Play preserves the selected preset/custom composition, multiplier, marker
positions and finite physical card order. It does not provision an extra deck for
the temporary Heroes. Legacy DELVE1 and omitted/custom Loot retain their existing
Engine resolution and placement compatibility.

## Verification and reproducible measurements

Focused tests cover defaults, dynamic All membership, restricted/Empty selection,
all abundance round-trips, deterministic placement, large unoccupied multi-room
Sites, occasional vacancy, stacked custom reservations, footprint/deployment
protection, ordinary reachability and unchanged population/drop definitions.
Quick Play tests compare exact finite card order and supply through transport.

Run the measurement tool from the repository root:

```powershell
dotnet run --project tools/WorldGenerationBenchmark --artifacts-path .world-benchmark -- --loot-abundance .playwright-cli/loot-abundance-corpus.json
```

It generates 108 cases: Grassland/Woodland/RockyWilds, 40x35/60x50, seeds
1/12/12345, Population Off/High at Difficulty 5, and all three abundances. Every
case is regenerated from its complete recipe. It checks unchanged population,
ordinary reachability, deployment/figure exclusion, uniform All membership,
finite supply, quantity ordering and exact Quick Play Loot preservation. The
JSON records recipe, marker distribution, rewarded/empty buildings/dungeons,
deck quantities and requested/placed shortfalls. It throws on a failed check.

The A1 baseline 108-case run completed every check, including recipe regeneration.
Each row below aggregates 18 recipes (142 building and 13 dungeon observations).
Repeated abundance observations are not independent geographic layouts.

| Population | Abundance | Mean markers | Inside / outside Sites | Rewarded buildings | Rewarded dungeons | Completely empty buildings / dungeons | Mean deck cards |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Off | Sparse | 6.67 | 120 / 0 | 73 / 142 | 9 / 13 | 69 / 4 | 9.33 |
| Off | Standard | 9.56 | 172 / 0 | 73 / 142 | 9 / 13 | 69 / 4 | 12.67 |
| Off | Plentiful | 19.11 | 344 / 0 | 73 / 142 | 9 / 13 | 69 / 4 | 21.67 |
| High | Sparse | 13.06 | 149 / 86 | 79 / 142 | 11 / 13 | 39 / 0 | 41.00 |
| High | Standard | 22.28 | 227 / 174 | 88 / 142 | 12 / 13 | 37 / 0 | 50.00 |
| High | Plentiful | 44.56 | 458 / 344 | 95 / 142 | 12 / 13 | 32 / 0 | 72.33 |

All 1,272 requested Site markers and 802 encounter markers were placed: no
placement shortfalls in this corpus. All six effective canonical card IDs were
present with equal copy counts in every deck; finite supply covered all markers.
Population and geometry remained unchanged across abundances. Population Off
demonstrates rewards independent of monsters, while its remaining empty Sites
demonstrate retained vacancy. More abundance does not force new Site selection.
These are availability measurements, not survival or item-power balancing claims.

## A2 follow-up

[A2](SITE_POPULATION_A2.md) now changes Site monster vacancy and occupation targets.
Alert semantics, global Unit limits, A1 Loot selection and gameplay behavior remain
unchanged. Re-running the A1 corpus against A2 reflects its new population, while
still checking the same Loot contracts. Healing content, resurrection and passage
widening remain separate tracks.
