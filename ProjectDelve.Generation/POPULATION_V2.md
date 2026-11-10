# Population V2 — dense and structured encounters

Population is a bounded setup pass after final Civilization, Routes and geometry
analysis. The output is an ordinary `ScenarioDefinition`. Neither encounter IDs,
Site ownership nor Alert links become gameplay state. Engine Dormant, Alert,
activation bags, legal choices and Orders retain their established semantics.

## Changed implementation boundaries

| Files | Responsibility |
| --- | --- |
| `SitePopulationIntent.cs`, `CivilizationModels.cs`, `CampsiteGenerator.cs`, `CivilizationGenerator.cs` | Typed authoring intent and varied physical camp construction. |
| `PopulationModels.cs`, `PopulationGenerator.cs`, `PopulationPlacement.cs`, `PopulationComposition.cs`, `PopulationTopology.cs` | Distribution, bounded allocation, roles, exact Groups/Orders, placement and authoring-only Alert diagnostics. |
| `PopulationLoot.cs`, `WorldGenerationRecipe.cs`, `NaturalTerrainGenerator.cs`, `WorldGenerationPipeline.cs` | Configured WLD1 deck, finite setup provision, materialization and previews. |
| `../ProjectDelve.Engine/Loot.cs` | Existing preset multiplication also permits All; no draw/exhaustion behavior changes. |
| `../ProjectDelve.Web/WorldQuickPlay.cs`, World Generator HTML/JavaScript | Temporary All provision, placement preflight reuse, deck selection and stage-correct diagnostics. |
| Population, pursuit, camp, Loot and Web integration tests | Dedicated fixtures and existing behavioral coverage updated for V2. |
| `../tools/WorldGenerationBenchmark/PopulationInvestigation.cs` and project link | Twelve-case generation/setup/gameplay/transport measurement corpus. |
| `../CONSTRAINTS.md`, `WORLD_GENERATOR.md`, `CIVILIZATION_SITES.md`, this document | Approved behavior and verified implementation limits. |

## Site intent and structured camps

`SiteInfo.PopulationIntent` carries `SitePopulationIntent`: layout identity,
role areas/anchors and cluster IDs, an open patrol path, minimum/maximum occupation
and Connected/Separated Alert intent. The existing `SitePlan`, private construction
draft, atomic commit, access ports and reservations remain the construction boundary.
Civilization describes spatial roles without assigning a Monster Type.

Camps have clearing-centered and elongated roadside families. Independent streams
vary placement, orientation, radius (2–4 in world generation), optional second table
in each wing, occupation and Alert intent. Existing Campfire, Table, grass and floor
tiles form a shared hearth and separate supply/resting wings. No prop catalog was
added. All usable ground must connect to the approach before construction commits;
existing reserved roads cannot be overwritten. Settlements and buildings retain
their previous geometry.

Population resolves final usable role areas. Camps request 6–16 Units, with density
limiting their upper size to 8/12/16. Sentries, central defenders, resting occupants
and optional patrols receive distinct Groups/Orders where appropriate. Occupation
is commonly Goblins, sometimes Zombies; higher difficulties can provide Skeleton
Archer sentries and Troll central defenders. No Site family changes Engine behavior.
One fifth of eligible Sites/buildings are deliberately omitted by seeded occupation
selection; geometry, spacing and budgets can leave additional Sites vacant.

Sentries/patrols use WakeDistance 5; central/resting camp occupants use 1. Resting
occupants can be Lying. All roles except patrol are Dormant. Connected placement
grows a friendly distance-3 network; separated placement keeps resting-wing cohorts
apart and omits central/patrol bridges. The final connectivity check includes
authored and already accepted friendly figures, including awake relays and complete
footprints. Later gameplay movement may join or split those components normally.
Stationary camp occupants cannot occupy patrol waypoints; an actual Ping-pong
regression verifies route completion while the remaining camp stays Dormant.

Concrete examples from the documented 120×120 High corpus, seed 12345:

- `campsite-1`: elongated roadside layout, ten Dormant Goblins in three Groups,
  with two initially separated Alert components.
- `campsite-2`: clearing-centered layout, thirteen occupants in six Groups,
  including Goblins, Skeleton Archers and Troll support. Twelve are Dormant and
  one patrol is Awake; all thirteen initially share one Alert component.

These are examples, not fixed layouts or preserved historical seed contracts.

## Independent population and distribution

Population does not require Civilization. Natural candidates use seeded 12×12
region weights: zero, two or ten, followed by weighted random candidate ordering.
This creates comparatively quiet patches and concentrated regions without placing
encounters at regular intervals. Candidate groups can extend across region borders;
quiet regions are a distribution preference, not a runtime safe zone.

Natural encounters mix single Monsters and compact 2–5-Unit groups. Existing coarse
Threat weights select canonical content according to Difficulty; summoners are
excluded from this initial-population policy. A rare difficulty-5 lone Red Dragon
is possible if its Unique and complete 2×2 placement requirements are satisfied.
Independent wilderness groups currently share a Type; camps can mix Types.

Lone Monsters are Dormant, Upright, WakeDistance 6, Automated and Normal. Natural
groups are also Dormant with Normal Orders. Building defenders use existing
Zombie/Troll/Ghost content and Guard Area. Road patrols are deliberately Awake
Grunts with Patrol Route/Ping-pong; Low selects at most one Road segment and other
densities at most two. A camp may separately provide a small active patrol.

Canonical Goblin, Zombie and Troll regression tests wake the actual figure, let a
known reachable Hero leave beyond WakeDistance, and advance ordinary activations.
Normal continues approaching without a Guard Area leash. A monster-side FoW test
then makes that Hero unknown and verifies it is not targeted. This is existing
behavior, not persistent pursuit memory or a guarantee after knowledge/reachability
changes. Waking never inserts tokens or expands captured participant membership.

## Density, bounds and failure policy

| Policy | Low | Medium | High |
| --- | ---: | ---: | ---: |
| Encounter target | area / 220 | area / 100 | area / 55 |
| Unit target/allowance | min(area / 45, 128) | min(area / 25, 192) | min(area / 14, 256) |
| Camp maximum | 8 | 12 | 16 |

Integer divisions round down. Global ceilings are 160 encounters, 256 generated
Units, 192 generated Groups, 1,024 placement attempts, 768 natural candidate anchors
and two million charged work units. Up to 128 detailed failures plus a target
shortfall summary are retained. These are generator resource policies. Difficulty
changes weighted composition and camp support, not a new encounter-size control.
The Unit target is an upper requested allowance, independent of the encounter
target: either can have a shortfall even when the other is met.

Independent encounters keep more than three Manhattan Cells between initial
footprints; cohorts within a compound have no such blanket separation. Initial
authored figures also participate in placement separation. Explicit protected
authoring areas, Objectives, access approaches, crossing footprints and natural
entrances remain protected. A guard may occupy another geometric chokepoint:
figures are temporary combat obstruction, not permanent terrain disconnection.
Population never edits terrain or Edges or sacrifices an existing world connection.

Local checks cover complete footprints, internal Edges, occupancy and open patrol
paths; authoritative per-Unit Engine placement preflights accepted proposals.
The complete scenario is validated at publication. Failed proposals publish no
figures, Groups or reservations. Requests that cannot fit are omitted or use a
reported smaller occupation above the camp minimum. No unbounded retry or fallback
terrain edit occurs. Current building portals certify only 1×1 access; larger
building defenders are explicitly omitted rather than assumed to fit through them.
Guard areas include complete footprints, not just anchors.

## Loot and finite supply

WLD1 records `-OEmpty|Classic|Fun|Experimental|FarOut|All` after Population and before
the seed. The browser exposes the existing presets plus Empty, without a copy-count
control. An omitted suffix selects Empty; new browser settings explicitly select
their deck. Selected supplies and connected optional ground receive up to 48 ordinary
Loot markers, normally about one per three accepted encounters. Loot is placed on
free passable ground in the largest final potential-traversal component; it does
not require a chest or generate fixed rewards.

`PopulationLoot.Provision` reuses setup-only `PresetMultiplier`, including All.
It selects enough whole copies for initial markers plus rounded-up expected initial
defeat drops, preserving every card's relative quantity and any larger existing
multiplier. Explicit Custom compositions retain their quantities; only their
unreserved remaining supply can receive new markers. Empty generates none.
Unsupported supply beyond the existing finite deck bounds produces an explicit
error; it never silently deletes markers or truncates a preset distribution.

Quick Play keeps All and repeats its effective composition, including local cards,
for the original markers and expected initial drops including its five Heroes.
The source world/Designer deck and markers remain untouched. Decks never replenish
at runtime. Expected drops are setup provision, not a guarantee against exhaustion
from summons or unusually many successful drops.

## Diagnostics and verification

Generation previews report intended/actual occupation, composition, per-Unit roles,
Groups/Orders, Dormant/Awake counts, initial token count, server-derived initial
Alert components, Loot and failures. Usable-area overlays include Site population
areas before occupation. Earlier stages/checkpoints do not display later Units.
Authoring diagnostics never enter player-visible FoW observations.

Focused fixtures cover vacant/occupied Sites, no-Civilization population, density,
composition, deterministic output, both camp families/topologies, actual patrol
progress, waking/Posture/Orders, pursuit/FoW, finite Loot, complete footprints,
chokepoints/protected space, bounded shortfalls, save and DELVE1 round trips.

Reproduce with restored dependencies, sequentially before browser inspection:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --population --fixtures .playwright-cli/population-v2
```

Resolve installed Node and Pillow-capable Python as documented in `AGENTS.md` and
`BROWSER_INSPECTION.md`. Use the protected Edge dispatcher, preserving port 5080.

Verification on 2026-11-10: cached solution build passed (NU1900 vulnerability-feed
warning only); all 2,754 Engine tests passed. Web had 722 passes and the same twelve
documented `ScenarioContentReferenceTests` failures for conflicting `knightly-boots`
identities. The focused World/Quick Play/Population/Alert Web run passed all 28 tests.
The full JavaScript and dispatcher-security command passed with installed Node and
Pillow 13.0.0 selected through `PROJECTDELVE_PYTHON`. No unrelated content-reference
tests or authored scenarios were changed to suppress those failures.

## Measurements and remaining limits

The benchmark covers 30×24, 60×50, 120×100 and 120×120 at all three densities,
with fixed seed 12345, F10/W4/R5, enrichment, four requested camps, one settlement,
two Roads, Difficulty 5 and Classic Loot. Generation, setup and public Round Start
boundaries use one warmup and median of three runs. A representative wake-up moves
a scout to a validated setup position, then resolves one actual Monster Group
through ordinary Automated decisions; this expensive gameplay sample is one observed
run, bounded at 1,000 decisions. It does not simulate an all-awake world.

Public Round Start measurements include validation, copies, FoW, proximity checks,
bag construction and the first decision; they are not isolated Alert timings.
Generated no-hostile boundaries and Quick Play with five Heroes are measured
separately. Total Unit count is not used as a proxy for ordinary activation work.
Quick Play setup in the raw corpus measures Engine materialization of the prepared
temporary scenario, not the cumulative browser preparation/import flow.

Large-board play can still be slow after waking. Source inspection identifies
repeated full-state copies, validation and attack-position analysis across board
Cells and Units as candidates for subsequent profiling; this milestone does not
change activation/behavior rules or add rendering optimizations. The recommended
next step is focused query profiling before raising the population ceilings further.

### Ceiling selection

The initial [512-ceiling study](../assets/previews/population-v2-ceiling-study.json)
produced 442 Monsters (433 Dormant) on 120×120. Its reviewed Debug-host Edge check
validated and rendered the world with no application errors, but generation took
23 seconds and the complete Quick Play preparation/import flow took 125 seconds.
That latency is too high for the normal resource policy. The retained ceiling is
therefore 256 Units, with Low/Medium ceilings 128/192 so density remains meaningful
on the largest maps. No Engine activation or behavior shortcut was introduced.

At the user's request, the [exact saved 442-Monster world](../assets/previews/population-v2-442-study.delve)
was rechecked without concurrent Codex tests or benchmarks. Three isolated Debug
Quick Play preparation calls took 37.37, 36.64 and 36.80 seconds; two imports took
84.40 and 85.68 seconds. Host CPU time closely matched wall time in each phase.
Their 121–123-second combined cost reproduces the original 125-second browser flow.
The [recheck data](../assets/previews/population-v2-442-recheck.json) distinguishes
HTTP phases from the original complete browser measurement. This establishes a
substantial application cost but does not prove that all external contention was
absent. These are Debug-host measurements; the detached corpus uses Release.

The same saved world was then tested with a cached Release Web build, sequentially
without concurrent Codex jobs. Three HTTP checks took 14.48/13.19/12.89 seconds to
prepare and 30.32/30.41/29.78 seconds to import: combined 44.80/43.61/42.67 seconds.
All returned HTTP 200. Thus the two-minute delay reproduces in Debug, while Release
substantially reduces it. The largest-world cost cannot be attributed solely to
the user's other activity, and the Debug number must not be presented as a Release
performance result. These repeated HTTP phase sums exclude browser rendering.

### Final resource-policy measurements

The [final twelve-case corpus](../assets/previews/population-v2-performance.json)
contains every measurement, component size, footprint-safe scout position,
serialization size and deterministic fingerprint. All cases completed without
validation errors or reaching the Automated decision limit. Times below are
milliseconds; D/A counts exclude the five temporary Quick Play Heroes.

| Board | Density | Encounters actual/target | Units D/A | Generation | Population | Quick Play Round Start | One woken Group activation |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 30×24 | Low | 3/3 | 12/3 | 117.9 | 23.5 | 5.1 | 261.5 |
| 30×24 | Medium | 7/7 | 24/3 | 100.4 | 28.7 | 13.2 | 269.5 |
| 30×24 | High | 13/13 | 34/3 | 136.8 | 39.6 | 4.6 | 103.7 |
| 60×50 | Low | 13/13 | 36/3 | 387.9 | 61.7 | 14.9 | 294.5 |
| 60×50 | Medium | 30/30 | 92/3 | 277.0 | 75.3 | 75.4 | 274.0 |
| 60×50 | High | 54/54 | 164/3 | 513.5 | 238.3 | 328.3 | 673.1 |
| 120×100 | Low | 41/54 | 123/5 | 624.0 | 160.2 | 174.3 | 3656.5 |
| 120×100 | Medium | 62/120 | 182/10 | 1137.7 | 451.1 | 576.5 | 2466.3 |
| 120×100 | High | 85/160 | 246/10 | 2177.2 | 1014.7 | 1288.6 | 3726.9 |
| 120×120 | Low | 43/65 | 125/3 | 649.9 | 155.2 | 177.9 | 2252.2 |
| 120×120 | Medium | 64/144 | 183/9 | 1060.5 | 388.1 | 499.4 | 2980.2 |
| 120×120 | High | 88/160 | 247/9 | 1912.1 | 853.0 | 1141.2 | 4319.1 |

Encounter shortfalls on large maps are explicit consequences of the selected Unit
allowances, rather than unbounded searches. The largest case has 100 Groups but
only three initial Monster tokens, 29 Loot markers and initial Alert components
of at most thirteen Units. Its scenario JSON is 719,695 bytes and DELVE1 export
28,513 characters; the preview response is 2,000,256 bytes and estimated retained
preview data 1,674,052 bytes. The Population stage allocates about 114 MB cumulatively;
that figure is allocation traffic, not retained memory. Dormant proximity work is
included in the public boundary timings; no isolated proximity-only timing is claimed.

### Designer and browser results

Fresh-context Edge checks covered 60×50 and 120×120, Classic/Enhanced previews,
stage changes, export, Designer import/validation, Quick Play, FoW and desktop/mobile
Tabletop presentation. The final largest-world Debug check took 8.1 seconds for
generation and preview, and 26.6 seconds for the complete Quick Play flow. Designer
reported valid; no console errors, failed requests or HTTP errors were recorded.
The earlier-stage check confirmed that later hidden Units are not shown. Dedicated
Web tests additionally start play and assert that hidden population IDs never enter
player observations.

The [browser report](../assets/previews/population-v2-browser-report.json),
[population preview](../assets/previews/population-v2-large-preview.png) and
[mobile Tabletop view](../assets/previews/population-v2-large-mobile.png) retain the
final evidence. The largest check used a separately reviewed localhost-only Edge
script because the protected dispatcher's ten-second waits were insufficient.
The browser's asynchronous Tabletop initialization can finish after the first
play-screen screenshot; the mobile capture confirms the rendered 3D view.

Remaining scope limits: natural small groups currently use one Type per group,
building defenders require certified 1×1 portal access, and performance samples
cover one fixed enriched recipe per size/density rather than all seeds. Large-board
Automated decisions and Debug-host Quick Play remain visibly slow. Profile the
existing authoritative queries before increasing ceilings; do not alter Dormant,
Alert or activation semantics to conceal the cost.
