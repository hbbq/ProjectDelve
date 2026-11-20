# World Credits placement and ground piles

Expedition regions can now contain physical Credits before the first activation. In a fixed sample of 72 regions, generation placed **1,040 Credits in 254 piles**. Five regions had none. In the separate twelve-seed gameplay sample, the Party collected **144 of the 237 world Credits** on the maps it actually entered. The highest observed Party balance was **33 Credits**.

## Placement

Each eligible location gets one percentage roll. A success can place one pile on a reachable Cell. Larger settlements and ruins can have several eligible rooms. Ordinary terrain gets only one opportunity across the entire region.

| Existing generation location | Chance per opportunity |
| --- | ---: |
| Mountain Dungeon treasure area | 70% |
| Other usable Cells in a Mountain Dungeon room | 35% |
| Ruin room or eligible exterior | 40% |
| Camp or fortified outpost room, supply area or eligible exterior | 30% |
| Settlement room or eligible exterior | 15% |
| Landmark | 10% |
| Open ground outside all Site footprints | 10% per region |

The generator reads published Site, Room and treasure-area metadata. Each distinct room gets one opportunity. Treasure Cells are excluded from the ordinary dungeon-room draw. Outside dungeons, a Site can also receive one exterior opportunity if it has supplies, a courtyard in a fortified outpost, or no rooms. That opportunity uses its supply Cells first, then its outpost courtyard, otherwise its exterior. Within those candidates, the generator selects uniformly among available Cells in the ordinary arrival-connected Land domain. The reachability check allows ordinary Doors to open.

Deployment Cells, protected population areas, Expedition endpoints, Machines, initial Unit footprints and previously placed Credits are excluded. Loot, Resources and Cell Markers can share the selected Cell. Failure to find a Cell omits the pile. The default cap is 24 piles per region. Placement draws no cards and reserves no Loot supply. [Generator and tuning](ProjectDelve.Generation/ExpeditionCreditsGenerator.cs)

| Amount on a successful Site draw | Probability |
| ---: | ---: |
| 3 Credits | 80% |
| 9 Credits | 18% |
| 25 Credits | 2% |

The single open-ground opportunity always gives 3 Credits on success. A 25-Credit cache can cover the 24-Credit Barbarian Level 2 price if the Party collects it and reaches an eligible Station. These defaults do not depend on current balances, Party size, Unit Levels or Equipment.

The 72-region generation sample contained 213 piles of 3, 39 piles of 9 and two piles of 25. Settlements held 109 piles, camps and outposts 66, ruins 30, ordinary dungeon areas 25, designated treasure areas 15, landmarks three and open ground six. These totals reflect how many eligible locations the sampled worlds contained as well as the per-location chances.

## Determinism and saved Expeditions

`ExpeditionCredits1` runs after terrain, Sites, routes, initial population, Loot and the service Machines. A regional stream shuffles the opportunity order so the cap does not systematically favor the first Site. Each opportunity uses its own stream derived from the local region seed, the policy identifier and its stable location ID. The site ordering and candidate Cell ordering are deterministic. These draws consume neither an earlier stage's stream nor the gameplay RNG.

The Expedition head records the policy and its validated `ExpeditionCreditsSettings`. Callers can pass settings to `ExpeditionRunner.Create`, including zero chances or a zero pile cap. Opening a legacy checkpoint restores its original board. Its first successfully published successor records the policy. The archive keeps its uncollected piles, and Side balances carry into the successor through the existing handoff. Ordinary scenario JSON, DELVE1, authored piles and save formats use the existing Credits representation. [Expedition adapter](ProjectDelve.Generation/ExpeditionRegionGeometry.cs) · [Runner](ProjectDelve.Web/ExpeditionRunner.cs)

## Tabletop and Designer

| Authoritative pile amount | Tabletop representation |
| ---: | --- |
| 1–5 | Two gold coins |
| 6–20 | Four gold coins |
| 21 or more | Seven gold coins |

Each coin has a low cylindrical body and a bright inset rim. The stack sits toward one corner, leaving room for unopened Loot, Coal and a Banana Peel. Two instanced meshes share the geometry. A depth-tested billboard carries the exact integer, faces the camera in both projections and scales with zoom. At distant overview scales its width is capped relative to a Cell. Close figures, nameplates and walls can occlude it. Hover and keyboard Cell inspection also expose the exact visible amount.

The renderer reads the Side's observed state and skips unknown terrain. Pickup and replay update the same retained board nodes. The tier has no Engine representation. Designer and the 2D DOM board views show a gold coin symbol and the numeric amount, with the currency name in the accessible description. Imported and manually authored piles use the same board renderer in playtest. [Tabletop geometry](ProjectDelve.Web/wwwroot/three-tabletop.js) · [Labels and cleanup](ProjectDelve.Web/wwwroot/three-viewport.js)

## Measured economy

Both studies use the published Credits Drop Content Pass catalog, the same twelve world seeds, the same starting balance and the same diagnostic decision policy. The generation sample covers indices 0–5 for every seed. Every pre-existing field in the 72 paired generation records matched the baseline, including per-Type populations, Machine requests, placements and omissions. The new sample ranged from 0 to 12 piles per region. [Generation export](docs/world-credits/gameplay.json.frequency.json) · [Drop-pass generation baseline](docs/credits-drop-content-pass/after.json.frequency.json)

Gameplay entered successors only after ordinary Party completion. The following totals count Credits on maps actually played, with opening banks excluded from income.

| Measurement | Drop-pass baseline | With world piles |
| --- | ---: | ---: |
| Regions played | 22 | 15 |
| Completed / Party defeated / capped | 11 / 7 / 4 | 3 / 3 / 9 |
| Generated as world piles | 0 | 237 |
| Generated by actual Monster drops | 133 | 112 |
| Collected by Party from world / drops | 0 / 120 | 144 / 88 |
| Collected by other Sides from world / drops | 0 / 9 | 27 / 18 |
| Earned by Party through confirmed sales | 9 | 0 |
| Uncollected world / drop Credits at region end or stop | 0 / 4 | 66 / 6 |
| Total Party pickup and sale income | 129 | 232 |
| Highest Party bank | 34 | 33 |
| Seeds reaching a bank of 12 / 24 / 50 / 75 | 6 / 1 / 0 / 0 | 10 / 4 / 0 / 0 |

The assessment starts a diagnostic ledger with the setup amount at each Cell. The first collection there includes the entire remaining world amount. Any excess comes from drops, including drops merged into that same pile. Later collections on the Cell come entirely from drops. Every region reconciles generation to Party collection, other-Side collection and physical Credits still on the board. The simulation also reconciles the Party's closing bank to its opening bank, pickups and confirmed sales. [Assessment implementation](ProjectDelve.Web.Tests/ExpeditionEconomyAssessment.cs)

The policy gives ground Credits priority over heading to the Exit and knows ground object coordinates, including unexplored ones. Its routes consequently diverged from the baseline and reached fewer successors. Nine regions hit the existing 40-round or 3,200-decision cap. This measures one reproducible scavenging policy, with unusually good information about where to search. No sample purchased Equipment, a Unit Level or recruitment. No Party reached The Tycoon's 75-Credit price. [Gameplay export](docs/world-credits/gameplay.json) · [Drop-pass gameplay baseline](docs/credits-drop-content-pass/after.json) · [Baseline method](docs/credits-drop-content-pass/ASSESSMENT.md)

### Reproduction

Two complete runs reproduced the 72 generation records and all 15 played-region records across the twelve seeds. Run from the repository root with .NET 10 and restored dependencies. The output directory can be changed to preserve earlier results.

```powershell
dotnet build ProjectDelve.sln -c Release --no-restore --artifacts-path .playwright-cli
$env:PROJECTDELVE_ECONOMY_SEEDS = '17,42,889,2026,77341,1,7,123,456,1024,8192,65535'
$env:PROJECTDELVE_ECONOMY_ASSESSMENT_OUTPUT = Join-Path $PWD '.playwright-cli/world-credits-repeat/gameplay.json'
dotnet test ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj -c Release --no-build --no-restore --artifacts-path .playwright-cli --filter FullyQualifiedName~ExpeditionEconomyAssessment.Export
```

## Verification and limitations

Paired generation tests compare serialized scenarios with the Credits field removed, Site geometry, construction metadata and deterministic regeneration. They cover five- and six-member deployment, populated worlds, preservation of unopened Loot, and accessible endpoints. A test-owned location fixture samples 2,000 local seeds. Persistence tests cover a real generated successor, a separately archived source pile, repeated Continue, save/resume and identical first-round replay. Projection and client tests cover hidden Cells, legal pickup, exact labels, disposal and camera sizing. [Generation tests](ProjectDelve.Engine.Tests/ExpeditionCreditsGenerationTests.cs) · [Handoff tests](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs) · [Visibility tests](ProjectDelve.Web.Tests/FogOfWarProjectionTests.cs) · [Viewport tests](ProjectDelve.Web.Tests/three-viewport.test.cjs)

The Edge walkthrough imported a 7 × 5 scenario through DELVE1 and opened it from Designer in playtest. The board showed 3, 12 and 75 together, with Loot, Coal and a Banana Peel alongside the 12. The Barbarian obscured its own 5-Credit pile at one camera angle. Rotation brought the amount back into view. In a live seed-17 Expedition a first-round Beastmaster move revealed 3 Credits at (15, 8). In seed 456 the opening Side projection omitted all six unexplored piles, worth 24 Credits in the materialized region. The inspected captures reported no console errors or failed requests. [QA fixture](ProjectDelve.Web.Tests/fixtures/world-credits-tabletop.json) · [Designer](docs/world-credits/designer.png) · [Desktop](docs/world-credits/desktop.png) · [Narrow](docs/world-credits/narrow.png) · [Expedition discovery](docs/world-credits/expedition.png)

Classic, Enhanced 2D and Three.js Flat 2D also displayed the imported amounts. The test suite covers the 5/6 and 20/21 boundaries and the maximum supported integer, including label replacement and removal. Full-suite results were 3,966 Engine tests, 923 Web tests and 1,174 Client tests passed. The Client suite skipped its optional Beacon benchmark. All seven browser-dispatcher checks passed. Concurrent .NET runs encountered existing HTTP and streaming-test timeouts. The full Web suite passed with test collections run sequentially. The cached Release build reported an unavailable NuGet vulnerability feed.

### Rendering cost

Each visible pile adds three drawable meshes per main rendering pass, including its label, with at most 674 triangles. Amount changes or collection dispose the old label texture, material and instance buffers. Coin geometry and materials are shared. A stationary pile does not schedule animation frames or add a local light.

| Edge sample | Draw calls | Triangles | Textures | Median render time | 95th-percentile render time | Camera frame interval, 95th percentile |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1,440 × 960, identical scene with piles removed | 27 | 644 | 10 | 2.5 ms | 3.6 ms | 16.8 ms |
| 1,440 × 960, four piles | 39 | 2,092 | 14 | 3.0 ms | 5.4 ms | 16.8 ms |
| 412 × 850, four piles, fresh browser context | 39 | 2,092 | 14 | 4.1 ms | 32.0 ms | 16.8 ms |

These are short whole-scene samples from desktop Edge. The narrow sample contained 21 renders and 15 camera-animation frames. Its timing includes startup. Physical phone GPU, thermal and battery performance remain unmeasured. At whole-region overview scale, a Cell and its label can occupy only a few pixels. Zoom or inspect the Cell to read its amount.

The Engine's existing signed-integer balance limit still applies on handoff. If a carried balance plus the successor's outstanding piles exceeds that limit, ordinary setup validation rejects the successor and retains the completed source.
