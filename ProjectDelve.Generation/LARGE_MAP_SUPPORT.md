# Large map authoring

World Generator maps can be generated, inspected, regenerated, exported as DELVE1,
opened and edited in Scenario Designer, saved as draft files or to Scenario
Library, and reopened. The initial target is 120×100; the supported ceiling is
120×120 (14,400 Cells), including rectangular windows and signed world offsets.

## Limits and configuration

`AuthoringLimits` is an immutable per-host resource policy. Set
`Authoring:MaxBoardSize` using normal ASP.NET configuration, for example
`--Authoring:MaxBoardSize 80` or environment variable `Authoring__MaxBoardSize=80`.
The default is 120; startup rejects values outside 1..120. Catalogs advertise the
configured value to clients. Designer operations, World Generator, scenario
imports, selected Library exports and game setup use that policy. An explicitly
lower policy also restricts dungeon requests before generation; DNG1/DNG2 still
have their own unchanged maximum of 50. No generator algorithm or recipe output
changed. WLD1 already supported detached 120×120 windows; its existing signed
coordinate/overflow contracts remain intact.

The ceiling stays at the existing detached WLD1 bound after measuring both large
sizes, exercising dense content and checking browser rendering. It is not a
promise of constant latency for arbitrary Units or gameplay on a large board.
Dimensions and content payloads have separate limits:

| Boundary | Limit |
| --- | ---: |
| World/Designer board | 1..120 per dimension, configurable downward |
| DNG1/DNG2 | Existing profile minimums and maximum 50 per dimension |
| DELVE1 uncompressed canonical JSON | 4,194,304 UTF-8 bytes, unchanged |
| DELVE1 string | 1,048,576 characters, unchanged |
| Library submitted and canonical scenario JSON | 4,194,304 UTF-8 bytes |
| Library HTTP request | 4,259,840 bytes, including envelope |
| Whole local Library file | 64,000,000 bytes, unchanged |

Library previously allowed only 2,000,000 scenario characters and a 2,100,000-byte
request. Its bounds now align with DELVE1, including a tested 120×120 board with
every Cell explicitly authored and 14,280 internal Walls. More elaborate content
can still exceed the payload budget and receives an error; nothing is truncated
or simplified. Other routes retain their existing Kestrel request policy.
Lowering a host's board limit leaves valid existing Library documents catalogable,
readable and deletable; it restricts new saves and materialization rather than
declaring the entire Library corrupt. Browser recovery storage remains quota
limited; the existing recovery warning and draft-file download remain available.

## Implementation and profiling decisions

The original World Generator investigation is retained in WORLD_GENERATOR.md.
This milestone first emitted detached terrain-only fixtures and measured the
existing code before changing its limits or client paths. Browser baseline routes
substituted those fixtures and the detached dimension ceiling; the real server
rejected 120×100 authoring under its old 50-cell policy. Subsequent workflow checks
used the real generator, Designer and Library endpoints on an isolated host.

Two quadratic traversals were confirmed. Ordinary appearance already indexed the
board but still called the linear `TerrainDefinitionAt` for every Cell. It now
resolves the tile from that existing local dictionary. Designer used repeated
`Array.find` over sparse terrain overrides inside several per-Cell presentation
queries. A synchronous full render now builds one temporary index, preserves
first-match semantics and removes the index in `finally`. It cannot survive an
edit, Undo, imported definition, save or replay. Canonical content resolution is
unchanged. Mutable Engine Board lists were not replaced or given unsafe persistent
caches.

Painting refreshes the affected cells/edges, their components, labels and inspector,
preserving the mounted grid and keyboard focus. Ordinary selection and navigation
also retain the board. Undo, import, resize and metadata changes still use the
existing full render. Preview zoom changes only display size and scroll position.
Three.js already batches terrain and required no rendering architecture change.
There are no new lighting features, gameplay rules or expanding-world systems.

## Detached measurements

Windows, .NET 10.0.12 Release, three warmups and median of seven samples. Default
Natural Terrain, seed `123456789ABCDEF0`, offset (-17,-13), 120×100. Phases are
independent operations, not additive stages. Public JSON/DELVE1 readers include
Engine validation. Content validation measures typed `ScenarioCatalog` resolution.

| Operation | Before ms | After ms |
| --- | ---: | ---: |
| World generation, including validation/fingerprint | 17.65 | 17.04 |
| Engine materialization | 7.10 | 7.35 |
| Typed content validation | 0.263 | 0.245 |
| Scenario JSON serialization | 2.24 | 2.39 |
| Scenario JSON read/validation | 27.38 | 30.34 |
| DELVE1 encode | 5.26 | 5.30 |
| DELVE1 decode/validation | 19.21 | 20.84 |
| Ordinary board appearance seed | 144.71 | 3.13 |

Generation/serialization were not optimized; their small differences are noise.
Across Default, Grassland, Woodland, Lake Country and Rocky Wilds, appearance took
131.65–148.08 → 2.23–5.05 ms at 120×100 and 192.16–209.35 → 2.80–4.76 ms at
120×120. All twelve cases (also 30×24 and 50×50) retain identical terrain
fingerprints, ordinary appearance seeds, world appearance seeds, lookup checksums
and DELVE1 transports. Default 120×100 remains 1,041,102 canonical JSON bytes and
36,863 DELVE1 characters. Default 120×120 is 1,190,311 bytes / 42,325 characters.

Raw Engine `TerrainDefinitionAt`/`TerrainAt` still scan mutable terrain lists:
three full-board passes cost approximately 626–1,107 ms across the large cases.
This synthetic test makes the remaining scaling cost explicit. It is not the
optimized appearance or Designer path.

## Browser measurements and verification

Headed installed Microsoft Edge, fresh temporary contexts, 1440×1000 desktop and
390×844 mobile viewport/touch emulation on the same Windows machine. This is not
a measurement on physical phone hardware. Browser timings are individual observed
wall-clock operations; they include automation and painting unless specifically
identified as DOM construction. Shader/image cold starts vary. No live game was
advanced; renderer observations used detached terrain-only state fixtures.

Before optimization, 120×100 Designer opening took 12.3–12.7 seconds, terrain
editing 4.0–4.7 seconds, edge editing 3.4–4.0 seconds. Preview construction took
198–199 ms. Library save/open was blocked by the old dimension policy. Draft-file
opening was not reliably timed in the original baseline and has no claimed speedup.

Final measurements, renderer resource stability and regression results are recorded
in the accompanying [measurement data](../assets/previews/large-map-performance.json).

| Final operation | 120×100 desktop | 120×100 mobile | 120×120 desktop | 120×120 mobile |
| --- | ---: | ---: | ---: | ---: |
| Preview construction | 198 ms | 196 ms | 223 ms | 221 ms |
| Designer opening | 3.76 s | 3.85 s | 4.83 s | 4.60 s |
| Terrain edit | 184 ms | 529 ms | 397 ms | 743 ms |
| Edge edit | 104 ms | 324 ms | 128 ms | 346 ms |
| Draft-file open | 1.60 s | 1.53 s | 2.01 s | 1.90 s |
| Library save | 0.97 s | 1.29 s | 1.07 s | 1.54 s |
| Library open | 1.84 s | 2.49 s | 2.28 s | 2.36 s |

Library timings include the growing test-owned library (approximately 16–23 MB),
not an empty store. Cold DOM loading remained around 1–2 seconds. Flat Three.js
camera draw P95 after repeated switches was 12–19 ms; frame intervals P95 were
33–50 ms. Tabletop cold transitions took about a second in the final Default
checks. The representative Woodland map had approximately 4.7 million instanced
triangles; terrain batching kept Tabletop at about 31 draw calls. No persistent
camera stall was observed in the short stress checks, though cold transitions,
full-board imports and Undo still produce main-thread long tasks.

Across three repeated switch/pan cycles per final context, textures (18), geometries
(6), programs (14) and materials (40) stayed constant. Estimated terrain texture
memory stayed around 5.08 MiB. Observed JS heap after stress ranged from 49 to
229 MiB; garbage collection was not forced, so this is not a peak process/GPU
memory bound or a long-session leak guarantee. Final FoW checks hid exactly 11,999
or 14,399 unexplored Cells and showed no hidden artwork. There were no page errors,
failed requests or missing textures in the successful workflow checks.

Captured examples: [zoomed desktop preview](../assets/previews/large-map-preview.png)
and [mobile Three.js](../assets/previews/large-map-mobile.png).
The complete Grassland, Woodland, Lake Country and Rocky Wilds 120×100 workflows
passed in both viewport sizes, as did the final Default 120×100 and 120×120 checks.
These exercise preview/export, terrain and edge editing, Undo/Redo, draft-file
save/open, real Library save/open, Engine validation and switching Classic,
Enhanced DOM, Three.js Flat and Tabletop. Final checks additionally exercise exact
DELVE1 re-import, scrolling preview zoom, camera pan/zoom, repeated renderer
switches and FoW masking. Captured images were visually inspected. Existing deterministic
deterministic visual-variant tests remain in the normal client suite.

## Automated verification

Solution build passed with zero errors. Cached dependency auditing emitted NU1900
because NuGet vulnerability data was unavailable; no packages were changed.

| Check | Result |
| --- | --- |
| Full Engine | 2,382 passed |
| Full Web | 680 passed; 12 existing content-reference failures |
| Full JavaScript and dispatcher security | 951 passed |
| Final large-map/DNG HTTP focused tests | 27 passed |
| Large rectangle/overlap/appearance focused tests | Passed |

The twelve Web failures remain `ScenarioContentReferenceTests` collisions involving
`knightly-boots`, already isolated and reproduced on the prior baseline in the
original investigation. No unrelated content or assertions were changed to suppress
them. New regressions cover 120×100/100×120/120×120 recipes, signed overlapping
windows, DELVE1, dense edges, durable Library restart, lower per-host policies,
payload refusal, DNG limits, Designer rendering/edit history/recovery and captured
visual-seed compatibility. `CONSTRAINTS.md` is unchanged.

## Reproduction and remaining costs

From the repository root, using restored dependencies:

```powershell
dotnet run --project tools/WorldGenerationBenchmark -c Release --artifacts-path .world-benchmark -- --fixtures .playwright-cli/large-map-fixtures
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
node --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
```

Resolve installed Node/Python as described in AGENTS.md and BROWSER_INSPECTION.md;
model preparation tests need `PROJECTDELVE_PYTHON` pointing to a Pillow-capable
Python. Start an isolated port-5081 host with disposable Library/Workspace paths.
The separately reviewed `tools/large-map-browser.cjs` takes the installed Playwright
package path, fixture directory, output directory and `baseline`, `after` or
`final`. Follow BROWSER_INSPECTION.md for approval of this custom script; do not
modify the protected dispatcher. It closes its own browser and contexts. `baseline`
is for old source profiling with detached responses; `after` exercises all five
120×100 presets; `final` checks both largest Default sizes and renderer stress.

Full initial DOM construction, Designer imports/Undo and synchronous recovery/history
serialization remain noticeable. Library operations validate and rewrite the whole
local document and slow as it grows. Raw Engine terrain queries remain linear;
future optimization should index a proven immutable resolution/query scope before
considering storage redesign. Longer real-phone and gameplay-heavy profiling would
be needed before increasing the ceiling or claiming a universal frame-rate target.
Chunk streaming and a new Designer architecture are not required for this milestone.
