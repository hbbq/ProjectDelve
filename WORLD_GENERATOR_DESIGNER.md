# World Generator Designer — Density Scaling V1

The existing World Generator dialog now puts the map ahead of diagnostics. This
is an authoring change; Engine rules, Fog of War, Designer handoff and Quick Play
remain authoritative through their existing paths.

## Settings and layout

Desktop uses a 250–300 px independently scrolling settings sidebar and a large
preview. At widths up to 850 px, a generated preview appears before settings.
Generate Preview and Generate Another stay in the sticky dialog heading; the
seed remains directly accessible in settings. Fit/12/24/48 px zoom, Enhanced 2D
and Classic preview modes, recipe loading/copying and exact DELVE1 export remain.
Tabletop 3D remains available through the existing Quick Play/Playtest renderer.

Five native `details` sections initially collapse: World & Terrain, Civilization,
Routes, Population and Terrain enrichment. Summaries update immediately after
input, preset selection or recipe loading. None of these disclosures changes
inputs, invalidates the generation session or recreates the map. Enter/Space
toggle summaries, Tab follows native control order, and focus has a visible outline.

Generation diagnostics initially collapse and reset closed after a successful
generation. They remain below the map and scroll within a 44-vh limit when open.
All stage selection, Previous/Next, Before/After, Changed Cells, formation,
connectivity, opportunity, Site, room, route and Population overlays remain.
Construction hierarchy, failures, family/fallback details, population traces,
terrain proportions and performance counters remain available. Density diagnostics
show requested, attempted, accepted and omitted counts with shortfall reasons.
The construction failure list no longer truncates the server's retained entries
at 24. Changed Cells is initially off; its control remains available.

A compact final-result summary and warnings remain outside diagnostics, even
while inspecting an earlier stage. Site omissions, unresolved/omitted routes,
Population failures and layout fallbacks are summarized. Route failures are not
labeled as failed Site construction. **Inspect diagnostics** opens the final
stage and focuses its selector. Generation/validation errors remain in the live
status area. Editing settings keeps the retained map and its exact export until
the next successful generation.

## Configuration classification

| Setting | Meaning and treatment |
| --- | --- |
| Campsites, Settlements | Independent content densities; bounded sublinear targets |
| Through routes | Boundary-spanning network abundance, scaled by world dimensions |
| Exploration links | Useful additional connections; separately calibrated network targets |
| Connect Sites | Independent connectivity request, not a density |
| Route profile | Surface/path geometry, including width |
| Route intensity, Water crossings | Preparation/disturbance allowances and permission; not route count |
| Construction preparation | Per-construction terrain/cost budget; unchanged |
| Terrain coverage, scale, enrichment intensity | Terrain proportions/features; unchanged |
| Population density | Already area-scaled encounters and Units; unchanged |
| Difficulty | Existing Monster selection/roles; unchanged |
| Work, attempts, Monster/Group caps | Hard resource/capacity limits; unchanged |

Population keeps encounter targets `min(160, area / divisor)` with integer
division and Low/Medium/High divisors 220/100/55. Unit limits retain divisors
45/25/14 and ceilings 128/192/256; Group cap is 192. Off requests zero. Civilization
does not raise these settings, and vacant settlements remain possible.

Actual density formulas, all size examples, textual WLD1 fields and the complete
120-case calibration are in [Density Scaling V1](ProjectDelve.Generation/DENSITY_SCALING_V1.md).
Targets are seed invariant; geography, layout families and placements vary through
the existing deterministic seed streams. Old numerical WLD1 density fields are
rejected. Current recipes round-trip canonically; DELVE1 captures exact scenarios.

## Verification — 2026-11-11

| Check | Result |
| --- | --- |
| Cached solution build | Passed; zero errors, cached NU1900 vulnerability-data warning |
| Final focused density, exploration and population interaction tests | 20 passed |
| Final full Engine/Generation suite | 3,163 passed |
| Final full Web suite | 739 passed; only 12 known `knightly-boots` content conflicts |
| Full client and dispatcher-security suites | 1,022 passed |
| Final focused World Generator client suite | 44 passed, including additional route-warning regression |
| Supplemental real Edge keyboard test | Passed Enter, Space, Tab, retained values/map nodes, no generation on toggle, warning visibility and keyboard stage navigation |
| Edge desktop and narrow scenarios | 30×24 Low, 80×80 Medium, 120×120 High, omitted requests, and no Sites |
| Quick Play | Tabletop 3D and Enhanced 2D with Hero FoW; returned to original Designer draft |
| Console and failed requests | No errors or failed requests in completed inspection plans |

The existing Performance Phases 1–3 test coverage ran with the broad suites.
Density derivation is constant-time; settings/disclosure changes do not generate
or rebuild the full world. Existing planning budgets and sparse stage replay are
unchanged. Calibration timings are single-run Debug measurements, not controlled
warm performance comparisons. The final corpus peaked at about 6.21 seconds.

One population interaction fixture initially failed because its old two-Campsite
configuration mapped to three requests at its size. Selecting Low restores two
requests; its occupation, alert topology and patrol assertions remain intact.
Model preparation tests initially hit the documented Python launcher failure;
using the installed Pillow-capable runtime via `PROJECTDELVE_PYTHON` made the full
client run pass. No unrelated content conflict was suppressed.

Browser plans used the protected dispatcher documented in
[BROWSER_INSPECTION.md](BROWSER_INSPECTION.md), fresh Edge contexts and isolated
port 5081. The dispatcher cannot send keypresses, so the supplemental
[keyboard test](ProjectDelve.Web.Tests/density-designer.browser.cjs) was separately
approved and executed using the installed Playwright runtime. It uses a fresh
context restricted to the isolated host. The original port-5080 host was preserved.

Evidence: [inspection records](assets/previews/density-designer-verification.json),
[desktop](assets/previews/density-designer-desktop.png),
[narrow](assets/previews/density-designer-narrow.png),
[120×120](assets/previews/density-high-desktop.png),
[Tabletop 3D](assets/previews/density-quick-play-tabletop.png),
[Enhanced 2D gameplay](assets/previews/density-quick-play-enhanced.png), and
[returned Designer snapshot](assets/previews/density-returned-designer.yaml).

## Changed files and boundaries

- Generation: new `ContentDensity.cs`; Civilization/Route settings and generators;
  route metadata, pipeline frames and `WorldGenerationRecipe.cs`.
- Web: `index.html`, `styles.css`, `world-generator.js` and
  `world-generator-state.js`. No new shell, renderer or persistence architecture.
- Tests: new `DensityScalingTests.cs` and `density-designer.browser.cjs`;
  `world-generator.test.cjs`; adapted Civilization, route, population, settlement
  and HTTP fixtures to the new typed settings.
- Calibration: `DensityInvestigation.cs`, benchmark entry point and existing
  benchmark recipe fixtures; recorded JSON-lines corpus and browser evidence.
- Documentation: `CONSTRAINTS.md` generator configuration semantics, README,
  World Generator, Civilization and Route documentation, density specification,
  and this Designer report. Gameplay rules are unchanged.

Small maps can saturate at one settlement, making adjacent levels request or
accept equal totals. More settlements can leave less room for Campsites; more
primary roads can consume protected placement space. Accepted counts therefore
need not increase monotonically. Exploration can omit every request when there
is no useful alternative. The existing planner reports these limits rather than
reducing layout/access quality or guaranteeing geographically impossible sites.

## Civilization Beacons

Civilization also requests standalone neutral Beacon Machines, independently of
Sites and Buildings. The current policy requests one per started 3,600 Cells,
bounded to one through four, with no additional Designer setting. Civilization
disabled produces no Beacons. Each request has at most 96 attempts on a dedicated
derived seed stream and an allowance of 25 neighborhood Cell checks per attempt.
Unsuitable windows may omit every request. Diagnostics expose Beacons placed,
omitted, attempts and work separately from Site planning.

A candidate requires a completely passable, unreserved, Edge-free 5 × 5
neighborhood clear of protected route corridors and recorded entrances. The
center receives Stone Floor and a Beacon atomically through the existing planning
draft and Engine validation boundary. The surrounding ring supplies a local
bypass for ordinary and 2 × 2 figures; narrow passages are rejected. Later route
planning, current walkable analysis and Population treat Machines as blockers,
while the underlying Stone Floor remains ordinary terrain.

Stage previews show a diamond overlay for each Machine and include its identity
in Cell inspection. Construction inspection filters these overlays to accepted
operations at the displayed point. The current WLD recipe and seed reproduce the
same Machines; DELVE1 exports retain the complete concrete scenario.
