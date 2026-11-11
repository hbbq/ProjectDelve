# Civilization V2 — Villages and Settlements

Civilization composes the existing `SitePlan`, `BuildingGenerator` and
`RoomLayoutGenerator`. It changes authoring geometry; Population runs after the
final terrain, Edges and Site connections have been resolved. Runtime scenarios
contain ordinary terrain, Walls, Doors, furniture, Units and Orders.

## Layouts

| Family | Spatial organization |
| --- | --- |
| Market Village | Open central square, buildings facing its edges, short approaches and exterior passages. |
| Roadside Hamlet | Lots front actual protected road Cells, on different sides and orientations; the connected road graph retains bends and forks. |
| Farmstead | Primary farmhouse, smaller barn/storage/work structures and a shared working yard. |
| Inn Compound | Larger communal inn with connected rooms, smaller service structures and a courtyard. |

`SettlementLayouts` supplies bounded candidate plans. A plan fixes public space,
lots, doorway orientation and access before any child building is constructed.
The building/room generators continue to own shells, actual portals, partitions,
furniture and local traversal checks. There is no second Site framework or city
simulation. Building purposes describe geometry and population opportunities;
they introduce no economy or building interaction rules.

Dimensions, proportions, rotations, setbacks, room counts, candidate locations and
family selection use derived deterministic streams. Logical lot keys remain
stable, independent of accepted-object counts. A road is sampled through its
actual connected Cells, not inferred from a north–south axis or a bounding box.
Roadside placement requires suitable existing route frontage; other families can
stand apart from roads and request a connection through the existing Site system.

## Terrain, access and publication

Prepare only public spaces, actual buildings, their immediate aprons and access
paths. Unused surroundings retain natural terrain. Preparation permissions and
aggregate changed-Cell/cost allowances remain authoritative. Existing fixed
geometry, reservations and route corridors remain protected.

Existing route Cells can join settlement access without becoming settlement
terrain writes or reservations. Direct doorway-path contacts publish ordinary
`SiteAccessPort` records and topology attachments; they need no redundant
connection request. Other Sites publish bounded external connection requests.

An accepted candidate must connect every usable room and exterior Cell through
ordinary potential passage, including opening Closed Doors. Final root commits
retain Engine validation; candidate loops use local checks. Failed private drafts
publish neither partial buildings nor reservations. The shared Civilization
budget remains 512 nested attempts and two million work units. Reduced world
settlements and omitted requests report reasons; direct generator callers can
require an exact building count and family.

`SpaceAccess` describes one-Cell potential traversal. Single-Cell portals do not
promise entry for large figures. Population still validates complete footprints
and internal Edges; current building defenders require one-Cell footprints.
Temporary Unit occupation is not treated as permanent terrain disconnection.

## Population

Sites publish `SitePopulationIntent` using actual room, entrance, storage and
public-space geometry. Building clusters have separate stable identities.
Public access paths are protected from population placement; other usable public
space remains available to encounters.

Each settlement uses one deterministic occupation alternative: vacant, independent
house encounters (with individually vacant buildings), or a structured compound.
The alternatives do not simultaneously populate the same settlement. Occupants
are selected by existing difficulty/composition policy, independently of layout
family. No civilians or new Unit Types are generated. Most occupants remain
Dormant; explicit patrol roles retain the Population V2 activity contract.

Groups and Alert connectivity remain separate concepts. Separate buildings or
encounters do not isolate Alert through Walls. Compound topology is checked
against final friendly placements, with bounded rejection/shortfall when spacing
cannot realize the intent. Geometry is unchanged when Population is Off.

## Preview and persistence

The existing construction inspector shows family, lot bounds, actual buildings
and rooms, public spaces, entrances, population areas, exact route ports and
fallback reasons. It consumes server-authored metadata; it does not infer rules
or geometry. Public spaces/access share the existing usable-space control, and
lots share the buildings control. Existing construction before/after replay and
final export remain authoritative.

WLD1 evolves in place. Current recipe, content and seed reproduce the result;
there is no historical layout migration or WLD2 layer. DELVE1 preserves concrete
scenario geometry and occupants. Site layout metadata remains authoring-only.
Quick Play uses ordinary FoW, and save/resume retains ordinary game state.

## Verification and measurement

Focused settlement tests cover each family, road geometry, exact entrances,
connected rooms, bounded failures, rollback and deterministic structural diversity.
Population tests cover vacant/individual/compound alternatives, protected access,
unchanged geometry and scenario/save roundtrips. Web coverage includes Quick Play
FoW filtering with settlements, and preview coverage checks the new metadata.

Run from the repository root with restored dependencies:

```powershell
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
# Use installed Node and PROJECTDELVE_PYTHON as documented in AGENTS.md.
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
dotnet run --project tools/WorldGenerationBenchmark -c Release --no-restore --artifacts-path .world-benchmark -- --settlements --fixtures .playwright-cli/settlement-v2
```

The settlement benchmark uses 30×24, 60×50, 80×80 and 120×120, four fixed seeds,
one warmup and three timed samples. It reports median total/Civilization time,
construction attempts and validation cost, accepted/rejected settlement counts,
building/room counts, family distribution, route attachments, connection requests,
fallbacks and a separate final Engine validation sample. Run sequentially without
concurrent tests or browser work; timings are observations, not test thresholds.

### Verified results — 2026-11-11

The final Release corpus requested two settlements and one camp per world, with
10% forest, 4% Water, 5% rock, enrichment, two Road requests and Medium Population
at Difficulty 4. Ranges below are across seeds 1–4, using the median of each case's
three timed runs. Final scenario validation is one separate sample per case.

| Map | Total ms | Civilization ms | Construction attempts | Buildings / rooms | Settlements accepted / requested | Final scenario validation ms |
| --- | ---: | ---: | ---: | --- | --- | ---: |
| 30×24 | 98–192 | 30–119 | 18–30 | 2–6 / 3–8 | 7 / 8 | 3.7–8.0 |
| 60×50 | 185–478 | 54–156 | 15–20 | 5–6 / 8–10 | 8 / 8 | 4.7–14.5 |
| 80×80 | 222–267 | 43–66 | 14–19 | 5–7 / 8–12 | 8 / 8 | 13.6–22.8 |
| 120×120 | 431–507 | 95–115 | 15–20 | 6–8 / 8–11 | 8 / 8 | 26.4–40.4 |

Construction attempts include nested buildings/rooms and camp attempts. The final
31 settlements comprise 13 Roadside Hamlets, nine Market Villages, five Inn
Compounds and four Farmsteads. All 31 have verified open route attachments (62
ports in total); 18 connector requests supplement direct road frontage. The one
omitted settlement is a second request on 30×24 seed 1. Ten accepted layouts
recorded family/count/frontage fallback messages. Four requested camps could not
fit on the crowded 30×24 boards. Some independent primary/exploration route requests
remain explicitly unresolved under existing terrain preservation policy; accepted
route corridors remain unchanged by settlement construction.

[Measured cases and concrete lot geometry](../assets/previews/civilization-v2-performance.json)
retain complete codes, fingerprints, per-case construction validation costs,
failures and route attachments. Construction validation ranges were 14–31,
22–129, 42–48 and 65–91 ms respectively. These include existing planning commits,
not a full-scenario check per layout candidate. Small-sample JIT/GC effects are
visible, especially in the first 60×50 case; this is not a latency guarantee.

Seed examples with that same configuration:

- 30×24, seed 1: a farmhouse and smaller barn around a 40-Cell working yard;
  the second settlement is omitted.
- 30×24, seed 2: two hamlets front a connected road crossing, with six buildings
  on different sides and short paths to actual Doors.
- 60×50, seed 1: a four-building market around a square and a two-building inn
  compound with a three-room communal building.
- 80×80, seed 3: a market and farmstead instead of the two markets from seed 1.

The focused seed test requires at least eight distinct layouts out of twelve after
subtracting global translation, plus at least two compatible families. Dedicated
fixtures force all four families and force route access through actual bends/forks.

| Verification | Result |
| --- | --- |
| Solution build | Passed; existing cached NuGet audit warning NU1900 |
| Focused Engine/Generation and Web | 59 + 14 passed |
| Full Engine, including Generation | 3,151 passed |
| Full Web | 739 passed; 12 documented pre-existing failures |
| Full JavaScript and dispatcher security | 1,017 passed |
| Browser | Designer validation, Quick Play FoW, Enhanced 2D and Tabletop 3D passed |

The twelve Web failures are `ScenarioContentReferenceTests` conflicts for Equipment
`knightly-boots`, already documented in the foundation/World Generator reports.
No unrelated tests or content were changed to hide them. The JavaScript run used
the installed Pillow-capable Python through `PROJECTDELVE_PYTHON`.

Browser evidence is local ignored QA output, captured through the protected
dispatcher using the repository's playwright-cli skill. PNGs were opened and
visually inspected, including actual Walls, Doors, room partitions, yards and road
frontage. Successful runs reported no console errors or failed requests:

- `.playwright-cli/browser-qa/inspection-NkAciq/`: metadata overlays, Designer
  validation, and the Quick Play FoW screenshot (named `quickplay-tabletop.png`
  by the initial plan, but this capture is Enhanced 2D).
- `.playwright-cli/browser-qa/inspection-rJZY84/`: farmstead and roadside previews,
  plus roadside geometry in Tabletop 3D and Enhanced 2D.
- `.playwright-cli/browser-qa/inspection-pUfoWi/`: market/inn geometry in both
  renderers and the matching family metadata snapshot.

Intermediate QA plans were corrected for renderer-specific camera controls and
fresh-preview timing. No dispatcher changes were needed. The isolated 5081 host
was stopped; the original 5080 listener/PID was preserved.

### Changed files

| Area | Files |
| --- | --- |
| Layout planning and declaration | `SettlementGenerator.cs`, new `SettlementLayouts.cs`, `CivilizationModels.cs`, `CivilizationGenerator.cs` |
| Fixed-lot sampling | `BuildingGenerator.cs` (one candidate for an exact shell/entrance, instead of 256 duplicate samples) |
| Access and diagnostics | `SiteRouteAccess.cs`, `RouteNetworkMetadata.cs`, `WorldGenerationPipeline.cs` |
| Occupation | `PopulationGenerator.cs`, `PopulationPlacement.cs` |
| Preview | `../ProjectDelve.Web/wwwroot/world-generator.js` |
| Engine/Generation verification | `../ProjectDelve.Engine.Tests/SettlementGenerationTests.cs`, new `SettlementLayoutTests.cs`, new `SettlementPopulationTests.cs`, `SiteConnectionsTests.cs` |
| Web/client verification | `../ProjectDelve.Web.Tests/PopulationV2IntegrationTests.cs`, `world-generator.test.cjs` |
| Benchmark | `../tools/WorldGenerationBenchmark/Program.cs`, new `SettlementInvestigation.cs`, new `../assets/previews/civilization-v2-performance.json` |
| Approved behavior and documentation | `../CONSTRAINTS.md`, `WORLD_GENERATOR.md`, `CIVILIZATION_SITES.md`, `POPULATION_V2.md`, this document |

## Limits and next milestone

Buildings remain rectangular, with one to three connected rooms and single-Cell
Doors. This milestone does not implement irregular polygon buildings, multiple
floors, a city simulation, Ruins, mountain Dungeons, labyrinths, economy, civilian
AI, new terrain/prop catalogs or new rendering mechanics. A suitable next milestone
is settlement playability evaluation: encounter/access quality across a larger
seed corpus, followed by separately approved Ruins behavior.
