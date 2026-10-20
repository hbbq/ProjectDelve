# Project Delve

Project Delve is a physical-first fantasy dungeon board game and a software reference implementation of its rules.

The physical tabletop game is the source of truth. The digital implementation exists to make the rules precise, testable, reproducible, and usable by different clients without introducing mechanics that only make sense on a computer.

## Design principles

- **Physical first.** Every game mechanic must be possible to perform and represent in the physical game.
- **Explicit rules.** Software implementation details must not silently become game rules.
- **Shared rules model.** All Units use the same underlying mechanics. Scenario-authored controller assignments are independent of Side and Unit Type.
- **Engine-owned legality.** The rules engine computes legal choices. Human players, Monster Behavior, tests, simulations, or future AI choose only among those legal alternatives.
- **Separate state and presentation.** Physical State, Rules/Game State, and presentation are distinct concerns.
- **Deterministic where appropriate.** Non-random rules resolution should be reproducible. Physical randomness is represented explicitly through replaceable random providers.
- **Do not speculate.** Architecture and extension points should be introduced when actual rules or game content require them.

## Rules and constraints

[CONSTRAINTS.md](CONSTRAINTS.md) is the authoritative living specification for game-design decisions and implementation constraints that have been established so far.

Implementation work should read it before making assumptions about the rules.

If a required behavior is ambiguous or deliberately deferred in `CONSTRAINTS.md`, do not invent a rule merely to complete an implementation. Surface the ambiguity so the game design can be clarified first.

The README intentionally does not duplicate the detailed rules from `CONSTRAINTS.md`.

The current implementation emits `UnitDefeated` for unreplaced defeat and retains the source Unit, Figure, Unit Type, and occupied Cells as event context. Undying replaces defeat at the shared lifecycle boundary, before any outcome snapshot exposes the result. Existing multi-recipient effects fix their recipients before application, and Attack follow-ups wait for the complete Attack.

No current Ability creates an automatic `When` consequence. FIFO queue draining, consequences created during draining, and defeat-triggered effects from Lying or removed sources therefore have no concrete content to resolve or exercise yet. No trigger framework or placeholder Ability is included. When such content is established, its consequences must finish after the current effect, drain in FIFO order, and finish before `After` follow-ups are offered, using retained defeat context where needed.

## Reference implementation

The reference implementation uses **C#/.NET**.

Its core rules engine is intended to be independent of UI, rendering, transport, and persistence technology. The game progresses as a persistent, stepwise state machine that can stop at stable decision points and later resume from serialized state.

Rules resolution produces authoritative Game State together with meaningful Rules/Domain Events. Clients may translate those events into text, animations, sounds, or other presentation without affecting the rules.

Decision Providers and Random Providers form explicit boundaries around agency and physical randomness. This allows the same engine to support human play, Monster Behavior, deterministic tests, simulations, and possible future AI-driven play.

## Current status

Project Delve now has a first executable reference-engine slice in `ProjectDelve.Engine`, with focused tests in `ProjectDelve.Engine.Tests`.

The slice resolves one Round at a time: `ActivationToken(TypeId, SideId)` values are drawn without replacement; each living Unit completes its whole activation before the next eligible Unit of that exact group is selected from current state. The engine pauses at legal Unit selections and typed activation choices. State contains the pending decision and can be serialized between steps. The Decision Provider selects an opaque supplied key: Move or Stay first, then an Action or End Turn, with applicable Free Actions alongside these choices. Stay completes Move; End Turn finishes the activation. A mandatory MoveAfterAttack continuation resolves before End Turn becomes legal and still allows `null` for staying; the Random Provider supplies token draws and physical die faces.

`GameState.Bag` is a list of immutable `(TypeId, SideId)` records. Round-start construction preserves Type authoring order and sorts each Type's participating Sides ordinally. `ActiveToken` and every decision's `Token` retain that pair. Each participating group requires an explicit controller assignment. Summoning uses an existing group assignment or inherits its source's controller; it never adds a token to the current round's bag.

`AttackInProgress` contains a narrow immutable `AttackContinuation`: source Action, selected Cell, ordered fixed targets and DEF counts, fixed ATK count, stage/index, shared rolled faces/Hits and completed target results. `DoorInProgress` retains the acting Unit, Door, success count and Action id. Costs are spent at commitment, before any roll. Each nonempty pool becomes one mandatory `DecisionKind.RollDice` with the opaque `roll-dice` candidate and result-free `DicePool` metadata. Only submitting that candidate invokes `IRandomProvider`; refresh and invalid/null input cannot roll. Defence belongs to each defender's controller. The committed Attack finishes even if its caster dies or becomes Lying, then qualifies follow-ups.

`RoundStarted`, `ActivationStarted`, `ActivationCompleted`, `ActionUsed`, `AttackStarted` and `DiceRolled` extend the existing RulesEvent stream. Creation, posture, damage and defeat outcomes carry source context where available. Every event still gets a detached `ResolutionStep.StateAfter`; no event sourcing or client-side state reconstruction is involved. `AbilityUsed` remains during transition for existing bonus-use consumers. Browser controls and dice text consume the generic projection; automated hosts submit the same candidate and no browser auto-roll preference is implemented.

The canonical Unit content is declared together in [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs). Its typed C# helpers are in `UnitAuthoring.cs` and translate into the existing runtime `UnitType` model. Normal Attack is implicit; `CantAttack()` removes only that Action. Limited uses and mechanic balance parameters are explicit in the roster, and automated Behaviors are attached separately. Existing `UnitType.Barbarian()`, `UnitType.Goblin()`, and other factories forward to this roster and retain custom-ID support. Named card content uses `Ability(name, Uses(n), mechanic)` or `Ability(name, Unlimited(), mechanic)`; the mechanic determines timing. Bonus abilities retain explicit stable ids separate from printed names. Shaman uses `SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying)` and the independent `UseSummon` selection Behavior. Summoning reuses the configured Unit Type already in the game or its canonical roster definition, and descriptions read the same type/posture configuration as resolution.

Displacer Demon combines unlimited Swap and Displace Bonus Actions with the same adjacent-friendly DEF aura as Cleric. Each Bonus Action is available once per activation without a per-game counter. Swap exchanges adjacent 1×1 figures simultaneously; Displace offers each adjacent enemy's current Cell and legal one-step orthogonal destinations. External movement ignores the affected Unit's MOV and Phase, and cannot affect another Unit with a large footprint. These operations emit `PlacesSwapped` and `UnitRepositioned`, rather than `MovementCompleted`. Automated Behavior ranks the engine's legal candidates in Swap → ordinary Move → Normal Attack → Displace order. Swap uses normal deterministic target ranking. Displace ranks complete target/destination pairs by greatest increase in distance from the Demon, then fewest Cells moved, then normal deterministic target ranking before destination board order. The Demon is available in the browser scenario designer's canonical Unit palette for human or automated playtesting.

`UnitType.Actions` defaults to Normal Attack. `UnitType.FreeActions` explicitly supplies Open Door on Barbarian and Rogue; the ordinary constructor and `UnitType.Hero(...)` do not grant it implicitly. Each Closed Door bordering an occupied Cell supplies one Open Door candidate before or after Move and Action. Opening emits `DoorOpened`, consumes no Move, Action, or Bonus Action opportunity, and regenerates choices from the resulting state. There is no Free Action phase or skip choice.

`UnitType.TryOpenDoor` supplies a reusable parameterized Action with a success count from 0 to 6. It rolls one ordinary D6 through `IRandomProvider.RollD6()`; faces 1 through the success count succeed. Every attempt consumes the single Action and emits `DoorOpeningAttemptResolved` with the roll and outcome; success also emits the normal `DoorOpened` event. `UnitType.Behaviors` can include `ApproachThroughClosedDoors`. DefaultAutomatedProvider interprets this preference by passing `closedDoorsTraversable: true` to the shared approach-distance query for both movement destinations and staying. The query defaults to treating Closed Doors as impassable and does not infer traversal policy from Unit Type identity or behavior. Actual movement and LOS still treat Closed Doors normally. Other providers, including human control, may ignore the Behavior. `UnitType.Zombie()` composes MOV 2, Melee, ATK 3, DEF 3, HP 1, Normal Attack, TryOpenDoor(2/6), and that Behavior. DefaultAutomatedProvider prefers Normal Attack, then TryOpenDoor, ranking doors by the opposite cell in top-left order. It does not choose ordinary Open Door.

The first scenario uses 1×1 figures on Floor cells. Floor is enterable and does not block LOS. Movement respects walls, closed and open doors, friendly pass-through, hostile blocking, and the top → left → right → bottom shortest-path tie-break. Normal attacks use the defined Melee/range rules, Attack and Defence Dice, damage, defeat, and figure removal. A Random Provider returns the die symbols; its physical implementation must preserve the Attack Die's 3 Hit/3 Miss and Defence Die's 2 Block/4 Miss face distributions. `SideId` determines friendship independently of Unit Type or who supplies decisions. Open doors do not block LOS, including corner passages. The engine rejects attack LOS involving other featured edges or an intervening hostile Unit, whose LOS effects are still undecided.

Run the automated tests with `dotnet test ProjectDelve.sln`. The engine targets .NET 10 and remains independent of UI and persistence. The immediate goal remains to use executable scenarios to expose rule ambiguities and evolve from concrete game content.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to play successive Rounds on a hard-coded 6x5 stone floor board with a Barbarian at `(4,2)` and two Grunts at `(1,2)` and `(1,3)`. The barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2; rows 0 and 4 provide open routes around its ends. Barbarian can open the door as a Free Action. The Console uses the same named definitions as the Web host.

Hero decisions use numbered engine-supplied choices. Choose the supplied Stay or End Turn option, or `q` to quit. An optional follow-up Move still uses `0` to stay. The authored Grunt group uses DefaultAutomatedProvider. Tokens draw automatically; the Barbarian controller confirms each owned dice pool through the numbered Roll Dice option. Positions, HP, deaths, and door state persist between Rounds. The host shows canonical movement paths and resulting rules events. There are no victory conditions.

## Repository artwork skill

Add new originals or replace existing originals under `assets/source/`, then
invoke `/artwork` in Codex for this repository. The reusable skill lives at
[.agents/skills/artwork/SKILL.md](.agents/skills/artwork/SKILL.md). In Codex clients
that use skill mentions, invoke `$artwork` or select it through `/skills`.
If it does not appear, restart Codex in this repository. This uses Codex's
[repository skill discovery](https://learn.chatgpt.com/docs/build-skills).

The skill inventories new, replaced and unregistered artwork, maps it to canonical
content, runs the existing preparation pipeline, updates Enhanced 2D catalog entries,
previews and relevant documentation/tests, and verifies the result. It recognizes
numbered terrain variants and supports Units, Terrain, Edges, and future categories
using their own rendering requirements. Originals, Classic View, fallbacks and the
deterministic terrain selection algorithm are preserved.

It asks about exact files before ambiguous mappings, competing portraits,
unexpected dimensions/composition, destructive cropping/distortion, conflicting
documentation or missing rendering support. Missing sources never trigger automatic
runtime deletion. Unresolved files and any unverified visual behavior are reported.

## Browser playtest host

Decision Commentary is optional presentation metadata on a selected legal decision. Both decision submissions and commands accept a trimmed `commentary` string of at most 280 characters. Streaming updates publish `kind: "commentary"` before that decision's rules events; legacy responses carry `commentaries` with `eventIndex` counting preceding rules events. Each entry identifies the decision, acting Unit, controller, Side and candidate key. Fresh baselines omit historical commentary. The host retains its enriched and key-only external selection callbacks for isolated adapter tests; the production Gambit broker uses the authenticated Resident polling contract described below. All browser transports present decision commentary from every player separately from gameplay events. Resident observations exclude commentary entirely. Automated flavor text uses fixed wording and consumes no gameplay randomness. Browser commentary starts hidden and follows event playback.

Run `dotnet run --project ProjectDelve.Web -- --urls http://localhost:5080`, then open [localhost:5080](http://localhost:5080).

The playtest **Board view** selector offers **Classic View** (the default) and **Enhanced 2D View**. Both use the same DOM board, supplied legal choices, selection and event playback. Switching changes only a board data attribute, so it also works during playback. The Level Designer keeps its existing rendering. Enhanced boards retain a minimum 60-pixel Cell size for readable names/HP and scroll when they exceed the available width.

Board appearance follows `VISUAL_STYLE.md` and lives in `ProjectDelve.Web/wwwroot/board-visuals.js` (content-ID mappings and fallback materials) and `board-enhanced.css` (scoped appearance and feedback layers). Add optional `image` entries under terrain kind, Unit Type ID, Machine Type ID, resource Type ID, or Edge kind. Paths are relative to the web root; the recommended destination is `wwwroot/art/`. Entries inherit category defaults, and unknown custom Unit Types retain portrait abbreviations and Type names. Machine Type IDs are included in final and intermediate browser projections. Artwork does not supply geometry, Side, Posture, ownership, legality or event semantics. The artwork catalog supplies the canonical Unit portraits including Chief Mechanic and Diamond Kart, three ordered textures each for Stone Floor, Grass, Water, Lava, Gravel and Asphalt, four procedural Rail shapes, and Wall/Closed Door/Open Door masters. Other content retains its existing fallback graphics.

Enhanced tokens compose a circular Side-colored ring, portrait, compact bottom nameplate and compact top HP badge. Side text is hidden on the token; the full Side remains available through the token tooltip, accessible label and Unit details. The nameplate uses nonblank `presentation.cards[unitId].individualName`, then the card's Type display name, then the scenario Type's display name/Type ID. Generated Unit IDs are only inspection identifiers. The existing game model has no individual-name authoring field: browser hosts may supply an explicit presentation-only name map to `BrowserProjection.Create(result, individualNames)`, which applies names to final and playback cards without modifying game state. This phase adds no naming editor or scenario schema. HP appears only for Max HP > 1. The active Unit has a triangle marker; the selected/focused Unit has a cyan outline; hover/focus, event targeting and Lying have separate treatments. Lying tilts only the portrait, adds a dashed ring and a `Lying` label, and keeps HP/name level. Nameplates use a consistent 9-pixel font on one line within the token width, truncating overflow with an ellipsis; full names remain in the tooltip and accessible label. Resources occupy the top-right Cell corner, clear of nameplates. Classic retains its original abbreviation, posture and HP layout.

Catalog examples (portraits, Stone Floor and Edges are shipped):

```js
boardVisuals.unit["barbarian-type"] = { image: "./art/units/barbarian.webp" };
boardVisuals.terrain.StoneFloor.variants = [
  { image: "./art/terrain/stone-floor/01.webp" },
  { image: "./art/terrain/stone-floor/02.webp" },
  { image: "./art/terrain/stone-floor/03.webp" }
];
boardVisuals.terrain.Rail.baseTerrain = "Gravel";
boardVisuals.edge.Wall = { image: "./art/edges/wall.webp" };
```

Portrait images use `object-fit: cover` in a circular clip; load failures preserve the abbreviation/icon. Terrain base, Resource and Machine artwork layers use CSS `background-size: cover` over the fallback material. Terrain bases rotate on a decorative pseudo-element; transparent terrain objects use a centered `object-fit: contain` image child. Edge artwork uses a proportional image child with loading/error fallbacks and 90° rotation for vertical use, as detailed below. Resource/Machine glyphs remain overlaid unless the catalog explicitly sets `glyph: ""`. Do not embed paths or image-loading logic in interactions. Variants are ordered arrays of descriptor overrides (`image`, `fill`, `detail`, `glyph`); category default, content entry, chosen variant, then optional direction overrides are merged in that order. Rail uses North/East/South/West; Edges use Horizontal/Vertical. No automatic image rotations change a Rail arrow.

**Deterministic variants:** the host emits optional top-level `visualSeed` as browser presentation metadata in HTTP responses, streaming baselines, events and boundaries. `BoardAppearance.DeriveSeed` runs once at initial setup and each scenario replacement/restart. It computes `"board-v1:" + lowercaseHex(SHA256(UTF8(canonicalJSON)))`. Canonical JSON is the compact array `[1, scenarioId, width, height, cells, edges]`. Cells cover the full Board in y-then-x order as `[x,y,terrainKindName,railDirectionNameOrNull]`, treating omitted terrain as StoneFloor. None Edges are omitted; endpoints are normalized in y-then-x order, then Edges are sorted by first endpoint y/x and second endpoint y/x, and stored as `[ax,ay,bx,by,edgeKindName]`. Property/list insertion order and explicit default terrain do not affect the seed. Resources, Units, Machines, controllers, sessions and gameplay RNG are excluded. Imports use scenario ID `imported`, so different imported board materials/geometries can have different seeds; identical initial boards intentionally share distributions. Initial terrain/Edge edits can reroll the distribution, but runtime board changes never recompute the seed.

For each terrain Cell, `terrainVariantIndex` hashes `JSON.stringify(["terrain-v1", String(seed), terrainContentId, x, y])` with FNV-1a over UTF-16LE bytes: unsigned 32-bit offset 2166136261, XOR each code unit's low byte then multiply by 16777619, then XOR its high byte and multiply again, using `Math.imul` and `>>> 0` after each multiplication. Take unsigned hash modulo variant count (zero for an empty list). This is independent of browser/runtime hash functions and randomness; fixed vectors cover ASCII and surrogate pairs. Array order/count is part of the visual asset contract, so changing it may change assigned artwork. Legacy responses without a seed use `scenario:${scenarioId ?? "legacy"}`. A future persisted explicit seed can replace the host's derivation at setup and use the same `visualSeed` field and selection algorithm; no engine or client interaction changes are needed.

**First artwork pack specifications:** use the visual brief in `VISUAL_STYLE.md`; review the pilot set together at actual board scale before expanding it. Use sRGB WebP (PNG when alpha/quality needs it), no animation, and serve local static files through the catalog. Do not embed names, HP, Side colors, rings, frames, UI highlights, coordinates or rules text. Lighting should be consistent and details should remain legible when reduced to roughly 50 pixels.

**Composite Terrain and rotation:** `Tree.baseTerrain = "Grass"` and `StoneFloorWithTable.baseTerrain = "StoneFloor"` reuse those base catalogs, including their existing three ordered slots and fallback materials. Each composite entry declares an `object` descriptor with three variants. Base variants use the base content ID with the unchanged `terrainVariantIndex`; object variants use `${terrainContentId}:object` as their separate ID. Neither count affects the other layer. These are presentation mappings only; Terrain kinds and properties remain authoritative and unchanged.

`rotation: true` explicitly opts a descriptor into deterministic 0°/90°/180°/270° rotation. It is enabled on Stone Floor, Grass, Water, Lava, Gravel (including Rail's base), Tree objects and Table objects. Unit portraits, Edges, track geometry, Machines and Resources have no such opt-in. `terrainRotation` hashes `JSON.stringify(["terrain-rotation-v1", String(seed), terrainContentId, x, y, layer])` with the same UTF-16LE FNV-1a byte operations described above, then uses `((hash ^ (hash >>> 16)) >>> 0) % 4 * 90`. The layer is `"base"` or `"object"`. Rotation uses neither variant counts nor gameplay RNG. It uses the existing host seed through refresh, rerender and playback without changing seed derivation or any Stone Floor variant vectors.

Only decoration rotates; Cell geometry, pointers, legal choices and playback targets stay on the original nodes. Terrain decoration is clipped to the Cell and stays below Units, Resources, Machine labels, coordinates and interaction feedback. Object loading failures retain the existing Tree/Table material silhouette over the base; successful loads clear that silhouette so the artwork's alpha reveals the base. Missing base textures retain their material fallback. Classic View hides the object child and uses its existing terrain styles; the Level Designer is unchanged.

The six immutable RGBA PNG originals under `assets/source/terrain/objects/` are 1254 × 1254, centered and already transparent, without profiles, EXIF orientation or animation. The full `tools/prepare-artwork.py` pipeline exports them without cropping, padding, alpha cleanup or retouching at 256 × 256 using the existing sRGB/Lanczos/WebP quality 85/method 6 settings. Alpha is verified byte-for-byte against the resized RGBA image (Tree originals intentionally include partially transparent interiors).

| Originals under `terrain/objects/` | Catalog object | Runtime paths under `wwwroot/art/` | Bytes in slot order |
| --- | --- | --- | --- |
| `tree-01.png` … `tree-03.png` | `terrain.Tree.object` | `terrain/objects/tree/01.webp` … `03.webp` | 26,106 / 25,584 / 26,460 |
| `table-01.png` … `table-03.png` | `terrain.StoneFloorWithTable.object` | `terrain/objects/table/01.webp` … `03.webp` | 9,958 / 11,226 / 11,210 |

All 39 source originals are registered; every runtime asset has a source. Existing runtime pixels/profiles were verified unchanged apart from ICC creation dates, and their original bytes were retained. The updated [catalog sheet](assets/previews/artwork-pack-01.png) preserves alpha when drawing previews. The [composite preview](assets/previews/composite-terrain.png) shows test-owned Grass/Tree and Stone Floor/Table combinations with independent slots and quarter turns. These sheets do not constitute browser approval. Run `python -m unittest discover -s tools -p test_prepare_artwork.py` in the pipeline's Pillow environment to verify object registrations, exact dimensions, sRGB profiles and alpha preservation. Browser layout review remains pending when no connected browser is available.

| Asset | Exact export size / ratio | Alpha and composition | Pilot quantity |
| --- | --- | --- | --- |
| Stone Floor | 256 × 256, 1:1 | Opaque, full bleed, strictly top-down, subtle variation and compatible tile boundaries | 3 ordered variants |
| Wall | 512 × 80 horizontal master | Full-length stone strip; transparent exterior; renderer rotates 90° for vertical Edges | 1 material, 2 orientations |
| Closed Door | 512 × 80 horizontal master | Same strip alignment; clear wooden slab; renderer rotates for vertical Edges | 1 design, 2 orientations |
| Open Door | 512 × 80 horizontal master | Transparent central gap with posts/threshold; renderer rotates for vertical Edges | 1 design, 2 orientations |
| Barbarian and Zombie portraits | 512 × 512 each, 1:1 | Square source, UI applies circular crop; opaque painted background preferred, alpha supported over the dark fallback; face/focal detail inside central 70%, reserve lower 30% for nameplate and upper 20% for HP | 2 portraits |
| Gravel / Rail | 256 × 256, 1:1 | Three opaque Gravel variants; four transparent SVG track shapes with exact edge ports and separate procedural arrow | Integrated; browser review pending |
| Coal / Diamond | 128 × 128, 1:1 | Transparent exterior and original margins; displayed in 24px top-right markers | Integrated; browser review pending |
| Diamond Press | 256 × 256, 1:1 | Transparent exterior; full-Cell object over Stone Floor; ownership and storage text composed separately | Integrated; browser review pending |

**Artwork pack 01 exports:** the user-supplied generated originals in `assets/source/` are seven opaque RGB PNGs, each 1254 × 1254 with no embedded color profile. `tools/prepare-artwork.py` uses build-time Pillow with WebP support (`python -m pip install Pillow`, then `python tools/prepare-artwork.py`). It treats untagged sources as sRGB, exports with an sRGB profile, uses Lanczos resampling, WebP quality 85 / method 6, and preserves any alpha losslessly. There is no crop, padding, retouching or runtime image dependency. The script checks complete decoding, exact dimensions and unchanged source hashes.

| Runtime path under `wwwroot/art/` | Dimensions | Bytes |
| --- | --- | ---: |
| `units/barbarian.webp` | 512 × 512 | 58,182 |
| `units/zombie.webp` | 512 × 512 | 45,764 |
| `units/worker.webp` | 512 × 512 | 44,096 |
| `units/geomancer.webp` | 512 × 512 | 51,430 |
| `terrain/stone-floor/01.webp` | 256 × 256 | 16,426 |
| `terrain/stone-floor/02.webp` | 256 × 256 | 16,080 |
| `terrain/stone-floor/03.webp` | 256 × 256 | 15,890 |

**Second portrait batch:** the six additional originals are also opaque 1254 × 1254 RGB PNGs without embedded color profiles, stored as `wizard.png`, `rogue.png`, `grunt.png`, `skeleton-archer.png`, `goblin.png` and `troll.png` under `assets/source/units/`. The same script and encoding settings above export them without cropping or composition changes. Catalog keys are their canonical `wizard-type`, `rogue-type`, `grunt-type`, `skeleton-archer-type`, `goblin-type` and `troll-type` IDs.

| Runtime path under `wwwroot/art/` | Dimensions | Bytes |
| --- | --- | ---: |
| `units/wizard.webp` | 512 × 512 | 49,144 |
| `units/rogue.webp` | 512 × 512 | 42,646 |
| `units/grunt.webp` | 512 × 512 | 44,848 |
| `units/skeleton-archer.webp` | 512 × 512 | 53,022 |
| `units/goblin.webp` | 512 × 512 | 47,576 |
| `units/troll.webp` | 512 × 512 | 63,150 |

**Remaining portraits and terrain integration:** eight new portraits and nine new terrain originals are opaque 1254 × 1254 RGB PNGs, without EXIF orientation, embedded profiles or animation. All use the same sRGB/Lanczos/WebP conversion without cropping. Each portrait source below is `assets/source/units/<name>.png`, resolves through the explicitly registered canonical `<name>-type` ID, and exports to `wwwroot/art/units/<name>.webp` at 512 × 512.

| Portrait source name | Runtime bytes |
| --- | ---: |
| `cleric` | 56,922 |
| `shaman` | 74,386 |
| `ghost` | 41,296 |
| `bomb-imp` | 35,876 |
| `displacer-demon` | 33,314 |
| `labyrinth-shaper` | 53,234 |
| `red-dragon` | 44,118 |
| `locomotive` | 45,448 |

| Sources under `assets/source/terrain/` | Catalog Terrain kind | Runtime paths under `wwwroot/art/` | Bytes in numeric order |
| --- | --- | --- | --- |
| `grass-01.png` through `grass-03.png` | `Grass` | `terrain/grass/01.webp` through `03.webp` | 18,984 / 17,766 / 18,308 |
| `water-01.png` through `water-03.png` | `Water` | `terrain/water/01.webp` through `03.webp` | 7,056 / 7,186 / 7,790 |
| `lava-01.png` through `lava-03.png` | `Lava` | `terrain/lava/01.webp` through `03.webp` | 15,768 / 15,302 / 15,146 |

Terrain exports are opaque 256 × 256 full-bleed textures. The full export reproduced the existing sixteen exports' decoded pixels and sRGB profiles, apart from ICC creation dates; existing runtime bytes were preserved. The Stone Floor byte counts above correct outdated documentation. All 33 originals have registered pipeline exports and catalog mappings; no runtime assets lack sources. There are no competing or blocked files in this batch. Grass/Water/Lava now select among three images using the existing hash; their former material-only presentation therefore gains per-Cell variation. Stone Floor keeps its three slots/order, and seed derivation, hash/version and gameplay RNG are unchanged.

The [contact sheet](assets/previews/artwork-pack-01.png) shows the portrait/terrain exports with 50-pixel circular portraits and 60-pixel terrain Cells beside larger references, labeled by portrait or terrain/slot; its height adjusts to the catalog size. It is an asset composition preview, not a browser screenshot. The pipeline fully decodes sources and exports, checks opaque base terrain and lossless Edge/object alpha, and compares hashes for all source files, including unregistered files. Lava is intentionally more vivid than the other materials; highlight readability over it still requires browser review.

**Edge artwork:** the three originals in `assets/source/edges/` are 1774 × 887 RGBA PNGs with no embedded color profile. Their visible strips occupy only the middle of the source canvas. `python tools/prepare-artwork.py --edges-only` exports this batch and its [orientation/contact preview](assets/previews/edges.png) without rewriting Unit or terrain files. The full pipeline also includes Edges. Pillow with WebP support is required; an optional local `.artwork-venv` can hold this build-time dependency.

The reviewed crop rectangles (left, top, right, bottom, exclusive) are Wall `(15, 325, 1766, 555)`, Closed Door `(16, 299, 1764, 573)`, and Open Door `(16, 295, 1763, 569)`. Outside these rectangles, source pixels are empty or stray alpha below 8/255; no visible masonry, posts or door leaves are removed. The script rejects changed source dimensions/composition for renewed review. Each crop is uniformly reduced with Lanczos and centered on a transparent 512 × 80 canvas. The Doors need this slightly taller master than the nominal 512 × 64 brief to preserve their proportions. There is no stretching, replacement painting or removal of the Open Door's stone rails. Encoding uses the same sRGB profile, WebP quality 85, method 6 and lossless alpha as the earlier packs; complete decoding, dimensions, profile, alpha and unchanged source hashes are checked.

| Runtime path under `wwwroot/art/` | Dimensions | Bytes |
| --- | --- | ---: |
| `edges/wall.webp` | 512 × 80 | 17,368 |
| `edges/door-closed.webp` | 512 × 80 | 17,294 |
| `edges/door-open.webp` | 512 × 80 | 14,888 |

The existing `Wall`, `ClosedDoor` and `OpenDoor` catalog keys resolve these masters. A decorative image child is centered on the authoritative Edge, scales proportionally to one Cell's length, and rotates clockwise 90° for `Vertical`. Edge geometry, hit targets and events are unchanged. Only Enhanced 2D displays the child; Classic and the Level Designer retain their existing rendering. Loading failures retain each Edge's fallback material, including Open Door's dashed outline. Successfully loaded artwork clears the material beneath its transparent pixels, keeping the opening visible. Artwork stays below Units/nameplates/HP and ignores pointer input; existing legal-choice, hover and door/event focus remain on the original Edge node. The preview includes all six orientations on a checkerboard and a small test-owned connected-wall/door sample. The Open Door source has stone rails above and below its transparent opening; review its readability at minimum Cell size in the browser.

Browser screenshot and performance baselines remain pending. Inspect long/truncated names, all Side colors, HP + Resource crowding, Lying and large bases, Machine storage labels, edge artwork in both orientations, keyboard outlines, and horizontal/vertical scrolling on narrow screens and 50 × 50 boards. Confirm face visibility under the compact HP badge, full Cell coverage, repeated terrain seams, and feedback contrast over Lava. No screenshot approval is implied by the DOM tests.

The Web host serves a plain HTML/CSS/JavaScript client and six server-owned playtest setups. One authoritative game lives in memory. **Start selected** replaces it with the selected scenario; **Restart current** recreates the current scenario. Both discard the previous state, including pending activations and follow-ups. During effects, either control requests replacement immediately; playback switches to the replacement when its baseline arrives. The host finishes its current synchronous engine call, skips subsequent automatic decisions, and publishes the replacement baseline; an external decision wait is invalidated immediately. Revision checks still protect concurrent clients.

| Scenario id | Name | Board | Initial content |
| --- | --- | --- | --- |
| `basic-combat` | Basic Combat | 8x8 | Barbarian; several Grunts |
| `goblins` | Goblins | 10x10 | Barbarian, Rogue; Grunts and Goblins |
| `archers` | Archers | 12x12 | Barbarian, Rogue; Grunts, Goblins and Skeleton Archers |
| `wizard-doors` | Wizard / Doors | 15x15 | Barbarian, Rogue, Wizard; Goblins, Skeleton Archers, Zombies and a Ghost |
| `resource-transport` | Resource Transport | 15x15 | Wizard / Doors setup with six Coal markers under Zombies and along routes near Doors |
| `full-party-trolls` | Full Party / Trolls | 15x15 | All four Heroes; Goblins, Skeleton Archers, Zombies and Trolls |
| `shaman-hunt` | Shaman Hunt | 15x15 | All four Heroes; Shaman, Grunt and a 2x2 Red Dragon, with no initial Goblins |

Basic Combat is the startup default. Select a setup, click **Start selected**, then **Start round**. The scenarios explicitly assign their current Heroes to human control and their Monsters to `DefaultAutomatedProvider`. Confirm each human-owned Attack, Defence, or door check with **Roll Dice**. Completed rounds wait for **Start next round**. These are test content with no victory conditions or progression. Exact maps and placements are editable definitions in `ProjectDelve.Web/PlaytestScenarios.cs`; the browser receives catalog metadata and authoritative game snapshots.

The separate **Railway** scenario (`railway`) has a directional Rail loop, a neutral Railway Side with No Objective, Upright and Lying collision examples, and a 2x2 Dragon blocking the return route. Heroes and Monsters are manually controlled so you can leave collision subjects on the tracks or move the Dragon aside. Start subsequent rounds to keep the Locomotive running.

The Level Designer's Terrain brush and selected-Cell properties let you set a Rail's outgoing direction. Side settings edit neutrality and No Objective independently. Scenario JSON represents these as `neutralSides`, `objectives` with `objective: "NoObjective"`, and Rail Cell overrides with `terrain: "Rail"` and `direction: "North"`, `"East"`, `"South"` or `"West"`. Rail Cells require an explicit direction; choose a non-Rail default terrain and paint the tracks as overrides. DELVE1 preserves this content.

Resources are authoritative `GameState.Resources` entries, each containing a `ResourceType` (stable `Id` and presentation `Name`) and a `Position`. `ResourceTypes.Coal` is the initial content. `PlaceResource` validates the Board Cell and the one-Resource-per-Cell limit; markers may share Cells with Units or any terrain. Scenario definitions accept an optional `Resources` list, preserved by JSON and DELVE1 transport. The playtest board and imported designer layouts display the markers, and Gambit observations include their Types and Cells.

Canonical Zombies declare `TransportResource()` independently of their unchanged Behavior. A one-Cell Unit with this capability transports its starting Resource during its own Move, including MoveAfterAttack, when the destination has no Resource. The relocation is included in the same movement snapshot. Stay, intermediate Resources, occupied Resource destinations, and external repositioning leave those markers in place.

Scenarios use a separate initial-setup data boundary: `ScenarioDefinition` contains a `BoardDefinition`, ordered `UnitTypeIds`, anchor-based Unit placements, and explicit agency per Unit Type + Side. The board has a default terrain plus sparse Cell overrides and internal Edges identified by `Position + Direction`, with only `Right` and `Down` directions. Placement supports initial Posture and an optional `InitialHp`; normal HP and all ability uses otherwise come from canonical Unit content. Runtime identity and activation progress are not scenario data.

The small `Scenario` authoring helpers expand groups and map partitions into that concrete definition:

```csharp
using static ProjectDelve.Engine.Scenario;

var definition = Define(
    board: Map(8, 8, cells: [Tile(3, 2, TerrainKind.Tree)]),
    groups: [
        Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 4)),
        Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(4, 4), At(4, 3))
    ]);
var state = GameEngine.CreateGame(definition);
```

`Define` derives normal Type order from first group occurrence; its optional `unitTypeIds` argument preserves explicitly authored order and unplaced Types. `GameEngine.CreateGame` resolves canonical IDs, validates data and normal full-footprint setup legality, and returns independent mutable round-zero state with deterministic generated Unit IDs. It does not draw tokens or invoke providers; `StartRound` still initializes the activation bag and advances play. Catalog presentation remains separate. The console detour and courtyard API fixture use the same creation boundary, including the courtyard's wounded Wizard and unplaced Goblin Type. `ScenarioDefinitionJson` serializes concrete initial setup; `ScenarioDefinitionTransport` wraps that JSON in portable `DELVE1:` strings. Browser imports enter the same creation boundary and restart retains the imported definition.

### Resource and Machine artwork

Coal and Diamond render as 24px top-right markers in Enhanced 2D. Diamond Press fills its Cell over the existing Stone Floor texture catalog, with its name, Owner Side and storage labels layered separately along the bottom. This base is presentation only; scenario terrain and Classic View remain unchanged. Images use `object-fit: contain` without cropping or distortion. Glyphs remain visible while loading and after errors; Classic retains glyphs. Unknown Machine/Resource IDs retain category fallbacks. Decorative images ignore pointer input, and choices remain on the authoritative Cell.

| Original under `assets/source/` | Catalog key | Runtime under `wwwroot/art/` | Export / bytes |
|---|---|---|---|
| `resources/coal.png` | `resource.coal` | `resources/coal.webp` | 128 × 128 / 3,598 |
| `resources/diamond.png` | `resource.diamond` | `resources/diamond.webp` | 128 × 128 / 4,786 |
| `machines/diamond-press.png` | `machine["diamond-press"]` | `machines/diamond-press.webp` | 256 × 256 / 21,962 |

These three new originals are 1254 × 1254 RGBA PNGs with genuine alpha, no embedded profiles, EXIF orientation or animation. The full `tools/prepare-artwork.py` pipeline exports them with the existing sRGB/Lanczos/WebP quality 85/method 6 settings and verifies lossless resized alpha. Originals keep their transparent margins. All 47 source files are accounted for: 45 direct exports plus the two railway material components. No runtime assets lack sources. Full re-export confirmed identical decoded pixels/profiles for all 42 previous WebPs apart from ICC creation dates, and their original bytes were retained. No variant counts, ordering, hash algorithms or seed lifecycle changed.

The updated [catalog sheet](assets/previews/artwork-pack-01.png) includes all three assets. The [Machine/Resource composition preview](assets/previews/machines-resources.png) shows 24px markers over Stone Floor, Grass and Gravel, plus full-Cell Press compositions at 60px and 100px. The preview uses test-owned samples and approximate labels; it is not browser approval. No connected browser was available for actual layout review; label crowding, overlays and image visibility still need that review.

### Gravel and procedural railway artwork

Gravel is ordinary passable, non-LOS-blocking terrain, available as a default or explicit override in the Level Designer and scenario JSON/DELVE1. It has no direction. Enhanced Rail reuses the Gravel base catalog. Existing terrain hash algorithms, seeds and variant counts/order are unchanged; Gravel and Rail now gain three material variants and independent stable base quarter turns.

| Original under `assets/source/` | Catalog / purpose | Runtime under `wwwroot/art/` |
| --- | --- | --- |
| `terrain/gravel-01.png` … `gravel-03.png` | `terrain.Gravel`; `terrain.Rail.baseTerrain` | `terrain/gravel/01.webp` … `03.webp` (256 × 256 sRGB; 32,208 / 31,870 / 31,240 bytes) |
| `railway/sleeper.png` | Repeated transparent wooden component | Embedded once per track SVG, with fully transparent margins trimmed and proportions retained |
| `railway/rail.png` | Steel palette/style reference | No direct bitmap export; exact steel paths generated from shared geometry |

Run `python tools/prepare-artwork.py` in the existing Pillow environment to export the material catalog, four transparent `railway/{straight,curve,tee,cross}.svg` overlays, generated `railway/fallbacks.css`, the catalog sheet and [railway contact/network preview](assets/previews/railway.png). `tools/railway_geometry.py` owns 256px Cells, 64px gauge (rail-center spacing), 8px rail width, edge ports at 96/160px, 96px sleeper width, 32px sleeper spacing and 128px centerline curve radius. Curves use concentric circular rails; T-junctions combine the straight and two branches; crossings combine perpendicular straight rails. Sleeper positions follow the same centerlines; curved spacing divides the quarter-circle into equal intervals near 32px. No rail bitmap is bent or stretched. SVGs are static local assets without a runtime drawing dependency. The fallback CSS embeds the same steel paths without the sleeper texture.

Each board render derives incoming connections from orthogonal Rail neighbors pointing into the Cell, combines these with its outgoing side, and selects/rotates a shape. No incoming connections adds the opposite side visually; reciprocal arrows leaving one distinct side also display a straight track. Direction/neighbor edits therefore update the shapes on the next render, including Play design and playback snapshots. No derived connections enter scenario or engine state. A separate CSS arrow rotates only with the authoritative outgoing direction, ignores pointer input, and stays above tracks but below Units, Resources, coordinates and interaction feedback. The base rotates independently. Classic and Designer retain their existing Rail arrows and styling. Enhanced Rail paints grid borders inward so adjoining track artwork reaches exact Cell boundaries.

All 44 originals were decoded and hashed; the five new sources have no profiles, EXIF orientation or animation. Gravel is opaque RGB; the two components are RGBA with real alpha. The 42 texture/portrait/Edge exports fully decode with expected sizes, alpha and sRGB profiles; the remaining two originals form the procedural material kit. All source hashes stayed unchanged and the 39 existing runtime WebPs retained their original bytes after verifying identical pixels/profiles apart from ICC creation dates. Browser tools reported no available browser; the raster preview and DOM/geometry checks are not browser approval. Inspect junction sleeper crowding, arrow contrast, seams and overlay readability on the actual board at 60px and larger Cell sizes.

Verification uses `python -m unittest discover -s tools -p test_prepare_artwork.py`, `node --test ProjectDelve.Web.Tests/*.test.cjs`, then sequential `dotnet test ProjectDelve.sln --artifacts-path .artwork-qa` and `dotnet build ProjectDelve.sln --artifacts-path .artwork-qa`. The isolated root output avoids locks held by a running web app.

### Asphalt and Chief Mechanic

Asphalt is ordinary passable terrain that does not block LOS, with no direction or intrinsic effects. Scenario JSON, DELVE1, browser projections and the Level Designer preserve it as `Asphalt`. Classic shows its dark surface and terrain label; Enhanced 2D uses three stable variants and independent deterministic quarter turns. Existing terrain hash algorithms, seeds and variant assignments remain unchanged.

Chief Mechanic (`chief-mechanic-type`) uses the canonical stats MOV 3, RNG 0, ATK 0, DEF 4, HP 3, Unique, CantAttack, Transport Resource, Give Resource, Operate and Summon Worker (Side limit 3). Its reusable Prefer Summon Worker preference selects the top-left legal summon placement during Action selection, before ordinary Worker priorities. Worker movement, Do Nothing and human choices retain their existing behavior. Unique validation and runtime placement/summoning allow one living instance of each Unique Type on each Side; Activation Tokens still group by Type + Side.

Select **Chief Mechanic Production** (`chief-mechanic-production`) for a small 8 × 5 Asphalt workshop with two automated Sides, one Chief Mechanic and an owned Diamond Press each. Blue starts on Coal beside an empty Press; Red starts beside a supplied Press. Summon Worker competes with Give Resource and Operate until the per-Side Worker cap is reached. Both Sides have No Objective. Controllers can be changed for manual choices.

| Original under `assets/source/` | Catalog key | Runtime under `wwwroot/art/` |
| --- | --- | --- |
| `terrain/asphalt-01.png` … `asphalt-03.png` | `terrain.Asphalt` | `terrain/asphalt/01.webp` … `03.webp`, 256 × 256 |
| `units/chief-mechanic.png` | `unit["chief-mechanic-type"]` | `units/chief-mechanic.webp`, 512 × 512 |

All four originals are opaque 1254 × 1254 RGB PNGs without embedded profiles, EXIF orientation or animation. The existing `tools/prepare-artwork.py` pipeline exports sRGB WebPs with Lanczos resizing, quality 85 and method 6. All 52 originals were fully decoded and their SHA-256 hashes verified unchanged. The source catalog now comprises 50 direct exports and two railway material components, with no missing sources. Full re-export confirmed identical decoded pixels for the previous WebPs, whose existing bytes were retained. The regenerated [catalog preview](assets/previews/artwork-pack-01.png) includes the four additions at Cell/token scale. No artwork was regenerated. Preview inspection and automated Classic/Enhanced interaction, fallback and playback tests do not constitute browser visual approval; no browser surface was connected for actual-board visual QA.

### Level Designer V1

Click **Designer** in the browser host to author the same `ScenarioDefinition` format visually. The browser retains a local draft, including temporarily invalid placements, and uses only generic grid editing and server-supplied Unit geometry. It does not evaluate Delve placement, terrain, Unique, HP, or agency legality. Reload discards the draft; export a DELVE1 string to keep it. Switching to Playtest and back retains it.

**Terrain** paints sparse overrides by click/drag; **Use default** removes an override. **Edge** paints internal Right/Down boundaries, including empty ones, with Walls, Windows and open/closed Doors; **Remove** erases a segment. **Unit** places the chosen canonical Type at its top-left anchor with arbitrary SideId and an explicit Type + Side controller. **Select** opens cell/edge/Unit properties. Overlapping Units remain editable through a chooser or the placement list. Unit properties include anchor, SideId, Posture and optional InitialHp; blank HP means canonical full HP.

Agency rows are per `(UnitTypeId, SideId)` and can be added, edited or removed independently. Existing pairs are reused on placement; rows with no initial Units are retained. First Type use appends its declaration, deletion does not remove it, imported Type order is preserved, and unplaced Types can be declared explicitly. Order is displayed; V1 does not reorder declarations.

**Validate** checks a snapshot through engine creation. **Export DELVE1** always validates before encoding. **Play design** exports, then uses the existing revisioned `/api/game/scenario/import` operation to create round-zero gameplay; click **Start round** to begin. It does not turn runtime state back into a draft. Errors leave the draft editable, and responses from an older draft/request are ignored. Edits mark it **Unchecked**. No validation requests run during painting.

The Web host limits designer boards and imported playtest boards to **50 × 50** before materialization. This is host/editor policy, not an engine game rule. Shrinking is refused if it would hide authored terrain, edges or any Unit footprint. DefaultTerrain changes retain explicit overrides. Import retains the decoded definition directly, including redundant overrides, explicit None edges, unplaced Types, zero-placement agency, optional HP and all list order.

The designer uses four stateless endpoints: `GET /api/designer/catalog`, `POST /api/designer/import` with `{ transport }`, and `POST /api/designer/validate` / `export` with the ScenarioDefinition JSON body. Catalog metadata comes from the engine's single `CanonicalUnitTypes` registry. Validation returns `{ valid, errors: [{ message, path }] }`; first-error reporting includes authoring locations where available. Import returns scenario JSON; export returns `{ transport }`. These operations never mutate the active game.

Browser tests exercise the actual editor modules using the small Node DOM harness:

```powershell
node --test ProjectDelve.Web.Tests/*.test.cjs
```

For manual acceptance, import a FullParty DELVE1 into Designer; check its 15 × 15 board, four initial party Units and concrete room edges. Paint terrain, change a Door, and select/edit a Unit. Validate, repair any reported issue, export, and click Play design. Confirm round-zero setup, start a round, then return to Designer and confirm the authored draft is retained. Room/line tools, undo, declaration reordering and reload persistence are deferred.

Map authoring uses `Vertical(x, top, bottom, kind)`, `Horizontal(y, left, right, kind)`, and `Room(left, top, right, bottom, kind = EdgeKind.Wall)` to generate complete lines or room sides. Doors and windows are explicit later entries such as `Edge(7, 3, EdgeDirection.Right, EdgeKind.ClosedDoor)`. `Map` applies later entries with the same Position and Direction while retaining the first list position for that identity. `Tiles(TerrainKind.Tree, At(5, 4), At(3, 12))` expands several positions into concrete Cell overrides. The resulting definition contains only concrete Cells and unique Edges; duplicate Edges or Cell overrides in directly constructed definitions remain invalid. Game creation rejects undefined directions, out-of-bounds positions, Right edges in the last column, and Down edges in the last row; there are no exterior-boundary edge definitions.

Across the catalog, the roster includes all twelve current Unit Types (stats in MOV / RNG / ATK / DEF / HP order):

| Type | Stats | Actions / special content |
| --- | --- | --- |
| Barbarian | 3 / 1 / 4 / 3 / 5 | NormalAttack, OpenDoor, Rage, Fury, Cleave |
| Rogue | 4 / 1 / 3 / 2 / 4 | NormalAttack, OpenDoor, Dash, Throwing Knife, Backstab |
| Cleric | 3 / 1 / 3 / 3 / 4 | NormalAttack, OpenDoor, Heal, Holy Wave, Aura |
| Wizard | 2 / 4 / 3 / 2 / 4 | NormalAttack, OpenDoor, Focus, Fireball, Telekinesis |
| Grunt | 3 / 1 / 3 / 3 / 1 | NormalAttack only; no special capability or behavior |
| Ghost | 2 / 1 / 3 / 3 / 1 | NormalAttack, Phase |
| Red Dragon | 2 / 4 / 4 / 4 / 8 | NormalAttack only; Unique; 2x2 footprint |
| Zombie | 2 / 1 / 3 / 3 / 1 | NormalAttack, TryOpenDoor(2/6), ApproachThroughClosedDoors |
| Skeleton Archer | 3 / 4 / 3 / 3 / 1 | NormalAttack, MaximizeAttackDistance |
| Goblin | 4 / 1 / 2 / 2 / 1 | NormalAttack, MoveAfterAttack(1), BackAwayAfterAttack |
| Shaman | 2 / 0 / 0 / 3 / 1 | Summon Goblin, Flee; no Normal Attack |
| Troll | 2 / 1 / 4 / 4 / 1 | NormalAttack, TryOpenDoor(4/6), Undying, ApproachThroughClosedDoors |

Early maps offer movement, flanks, retreat space and broken sight lines. Wizard / Doors encloses a five-by-five room with two gates: Zombies try the Doors while a Ghost near the southwest corner can phase through the Walls toward the Heroes, still needing ordinary LOS to attack. Full Party / Trolls adds another broad gated room and partial partitions, leaving several routes through the central area. Shaman Hunt uses staggered walls, open Doors and obstacles; the Shaman remains reachable and flees through ordinary terrain paths. Its first spawned Goblin begins Lying and enters a later round's bag only through the normal snapshot rules. No scenario adds special rules.

Figures support exactly 1x1 and 2x2 footprints. Position is the top-left occupied Cell, movement paths contain anchors, and a large base remains one Unit for activation, HP and effects. The engine validates every occupied Cell and internal Edge. Shaman Hunt places Red Dragon in its broad eastern area near the partitions, using ordinary Default Monster Behavior without Dragon-specific mechanics.

The responsive layout caps board width by the viewport height on desktop, gives the board a larger column beside the debug panel, and stacks the panel below at narrow widths. Explicit shrinkable grid tracks keep cells square; percentage positioning aligns figures, edges, movement highlights, and click targets. Cell labels, figures, and edge thickness scale with the board. Small cells retain coordinates and terrain tooltips even when their text is compact. Authoritative figure geometry and placement previews come from the browser projection, including progressive snapshots. Any occupied Cell can identify a large Unit, while Cell-targeted choices remain distinct beneath its base. No game rules run in JavaScript.

Events expose token Side, activation start/completion, generic Action use, Attack start and actual dice faces. They play sequentially: movement follows the engine's canonical path, attacks show the supplied Hits/Blocks/Damage, death removes a figure, and door opening changes the visible edge. HP and round/current-Unit labels synchronize to the authoritative snapshot after playback. **Skip effects** ends playback quickly without submitting an unresolved RollDice; uncheck **Animate events** to use immediate rendering. **Refresh** reconnects to the current authoritative baseline without replaying old events. Events arrive over SSE and playback starts while the engine continues resolving; animations never gate engine progression. Received playback continues during an external decision wait. The browser performs no movement, combat, activation, or Monster rules.

You can choose directly on the board: click a highlighted movement destination, an attack target's figure or cell, or a highlighted door edge. During Move, clicking the current Unit chooses **Stay here** when available and unambiguous. Hover labels identify the choice; highlighted targets also support Tab and Enter/Space. Board controls submit supplied candidate keys and are disabled during requests and playback. The action area shows only choices without a primary board or Unit Card control, including End Turn and ambiguous targets.

The original HTTP endpoints remain compatible batch commands and share the same single session runner as the browser streaming interface:

| Endpoint | Request | Response / behavior |
| --- | --- | --- |
| `GET /api/game` | None | `{ revision, result, autoChooseSingleRelevantChoice, scenarioId, scenarios, presentation }`, with current State/NextInput and an empty Events list; no side effects. |
| `POST /api/game/scenario` | `{ "expectedRevision": 0, "scenarioId": "goblins" }` | Discards the old game and creates the selected setup at round 0. Unknown ids return `400`. |
| `POST /api/game/restart` | `{ "expectedRevision": 1 }` | Creates the current scenario from scratch, including Units, ability uses, Doors and empty initial bag. |
| `POST /api/game/round` | `{ "expectedRevision": 0 }` | Starts a round and advances automatic Monster decisions to a player choice or round end. |
| `POST /api/game/preferences` | `{ "expectedRevision": 1, "autoChooseSingleRelevantChoice": false }` | Changes relevance-based auto-choice and rebuilds all legal choices; defaults to enabled. |
| `POST /api/game/decision` | `{ "expectedRevision": 1, "candidateKey": "3,2" }` | Resolves the selected engine candidate and advances Monsters to the same stopping boundary. |

Mutation responses use the same `{ revision, result, autoChooseSingleRelevantChoice, scenarioId, scenarios, presentation }` envelope. `result` serializes the existing `EngineResult`, with ordered events accumulated across automatic Monster advances. JSON properties are camelCase and enums use their C# names. Candidate keys are opaque; submit the supplied key, or explicit `null` when `AllowsNone` permits it. All mutation request fields are required. Normal activation choices have explicit Stay/End Turn candidates and do not accept `null`. The server retains authoritative state and rejects illegal choices with `400`, or stale revisions and invalid lifecycle operations with `409`. Concurrent mutations are serialized. A failed or uncertain browser submission fetches the current snapshot rather than retrying the decision automatically.

The browser uses `GET /api/game/updates` and `POST /api/game/commands`. SSE frames have `event: update`, `id: <sessionId>:<sequence>`, and a JSON envelope containing `sessionId`, `sequence`, `kind`, `status`, `revision`, optional `decisionId`/`commandId`/`error`, and `snapshot` in the existing GameResponse format. Event envelopes contain one event and its detached authoritative StateAfter at event index zero. Boundaries contain the stable state with no historical events; only `waiting-human` boundaries offer an actionable choice. `resolving`, `waiting-external`, `idle`, and `faulted` distinguish the other session conditions.

Commands supply a unique `commandId`, current `sessionId`, `expectedRevision`, `operation` (`round`, `decision`, `preferences`, `restart`, `scenario`, or `scenario/import`), the operation's fields from the table, and `decisionId` for decisions. HTTP 202 means validated and reserved, with `{ commandId, sessionId, accepted: true }`; it contains no playback state. The same retained command ID and identical request returns its original acknowledgement without re-execution; conflicting reuse returns 409. The in-memory retry ledger retains 1024 commands; unchanged older requests fail their stale session/revision check. The browser installs state only from the stream, and offers decisions after received playback reaches the matching stable boundary.

A fresh subscription receives the latest authoritative baseline. Native EventSource reconnect sends `Last-Event-ID`; the host atomically replays retained updates and attaches the live subscriber. The journal retains 256 updates. Invalid, expired, future, or previous-session cursors receive a current baseline and skip unavailable animations. Slow subscribers disconnect rather than blocking the engine, then recover by the same replay/baseline route. SSE sends periodic comment heartbeats, uses no-store responses, and sends the X-Accel-Buffering: no hint to compatible proxies. Reloads recover current state and pending choices; game state and replay history are in memory and do not survive a server restart.

Scenario replacement, import, and restart rotate session identity. A replacement accepted during resolution finishes the current engine call, discards subsequent automatic decisions, then publishes a new-session baseline. It invalidates any external decision wait immediately. Resolution faults publish an explicit fault view from the last committed stable boundary; intermediate event snapshots are presentation data and never resume the engine. Earlier successful engine calls within an unfinished command also roll back to that committed boundary, with no automatic random retry. Restart remains available after a fault.

The host owns one serialized progression runner and a separate publication journal; engine rules remain in ProjectDelve.Engine. A host-only asynchronous automated-selector seam is exercised by tests: it receives a detached stable request/state plus session/decision identity, chooses among supplied candidates, and submits its eventual key through the existing engine API. It releases the runner during the wait and rejects stale completion after reset. The production Gambit broker additionally binds selected Unit Type + Side tokens on one actual Side, authorizes inference admission and stable receipt reconciliation, and preserves recoverable controller errors. Resident connects by polling; the engine retains its existing Human/Automated controller enum and has no Resident dependency. Human and Default Behavior adapters remain selectable.

Every supplied legal candidate carries engine-evaluated `relevant` metadata. Rage is legal whenever the active Barbarian has uses and Rage unused in this activation. It is irrelevant before Move, after Action, or when no legal Attack can benefit from its modifier; after Move (including Stay), it is relevant when an Attack can benefit. The **Filter irrelevant choices (display only)** checkbox hides candidates with `relevant: false` locally, without a server request or any change to submission legality. The separate **Auto-choose single relevant choice** checkbox controls the backend/session preference `autoChooseSingleRelevantChoice`, outside physical/rules GameState. A sole legal candidate with no option to do nothing resolves automatically, except `RollDice`, which always requires explicit provider submission regardless of relevance or this preference. When enabled, the preference additionally auto-selects exactly one relevant candidate among multiple legal candidates in a required choice. When disabled, such multiple-choice requests stop and expose all legal candidates to the provider. An empty optional request still resolves to none; multiple legal choices with no relevant candidates still require external input. Submission validation always uses all authoritative legal candidates.

Per-unit `MoveDone`, `ActionDone`, and `BonusActionsUsedThisActivation` and the narrow `MoveAfterAttackAllowance` continuation survive serialization. `Pending` is informational: choices are rebuilt from authoritative state on resume. Old saves containing type-only `Bag`/`ActiveTypeId`, group `Phase`, or the shared `BonusActionUsed` flag are rejected; no migration is provided. Each Bonus Action ability may be used once per activation, independently of the others. The activation usage set uses ability names, the existing content identities, and clears at activation end; persistent remaining uses do not reset. Rogue can use both Dash and Throwing Knife in either order, combining MOV +2, RNG +2, and ATK -1.

Barbarian explicitly supplies a `BonusActionAbility` composed of its content name, maximum uses, and `ModifierThisTurn(Atk, 2)`. `UnitType.CreateUnit` initializes its per-Unit `BonusActionUses` to 2/2; bare scenario Units are initialized once by the engine at the first round. The immutable usage value enforces `0 <= RemainingUses <= MaxUses`. Using Rage immediately spends one use and records Rage as used in this activation, even if no Attack follows. `ModifiersThisTurn` belongs to the current activation and is cleared when it ends. `GameState.EffectiveAtkOf` supplies both Attack legality and dice counts; the serialized `effectiveAtk` map supplies the browser's displayed effective/base ATK without client calculations. The unit panel also shows remaining/max uses. To test legal-but-irrelevant Rage, disable relevance auto-choice and uncheck display filtering.

Run the browser renderer and interaction checks with `node --test ProjectDelve.Web.Tests/door-choice.test.cjs`.

The host routes each decision using its controlling `ActivationToken` and the state’s explicit `ControllerAssignment` list. The current adapters map `ControllerKind.Human` to submitted input and `ControllerKind.Automated` to the default provider; these serializable values contain no provider instances. The Engine and Console have no Web dependencies. Web tests cover the production catalog, fresh independent setups, restarts, activation/follow-up replacement, Shaman spawning and round bags, and the HTTP controls. Detailed courtyard interactions retain the old map as a test-only fixture. Run the full suite with `dotnet test ProjectDelve.sln`; renderer checks use the Node command above.


The shared Unit Card shows the active Unit, retains the most recently active Unit, and temporarily inspects a board Unit on hover. `UnitType.DisplayName` and domain-owned `CardEntryDescription` records supply names, categories and printed rules text for Actions, Free Actions, Bonus Actions, passives and playable follow-ups. Monster Behavior remains decision policy and is excluded from these entries. Concrete rules and usage counters retain their existing engine representations.

The API envelope also includes `presentation`, alongside the observable `result` (future World Deck draw order excluded). `presentation.cards` joins domain descriptions to current Unit counters. `presentation.decision` projects every authoritative candidate into `Direct`, `Unit`, `Position` or `Door` selection with an opaque key, supplied label, optional card-entry reference, relevance and separate `affectedUnitIds`. Optional decisions have a supplied `noneChoice` whose key is null. These interactions select complete candidates; the browser never constructs targets. Direct choices can appear on the matching card, with a panel fallback during hover inspection. The browser chooses control placement, abbreviations and generic previews without interpreting ability names or effects.

`presentation.events` supplies text and generic outcome roles; attack summaries are distinguished structurally from target results, independently of ability names. Events remain aligned one-to-one with raw engine events. `presentation.resolutionSteps` supplies card data at the same event indices as the existing authoritative `StateAfter` snapshots. Base/effective stats and progressive HP still come from those snapshots, not event reconstruction. No renderer-specific markup, colors, animation instructions or presentation cues are part of the domain/API contracts.

Barbarian's concrete `Fury` passive adds ATK +1 when at least two living hostile Units are adjacent. Side inequality determines hostility. Fury and Cleric Aura reuse the same eight-cell adjacency and normal LOS query as Goblin threat detection. `GameState.EffectiveAtkOf` derives Fury from the evaluated state's Units, positions, and board on every call, independently in copied/hypothetical states. Fury creates no stored modifier and stacks additively with Rage: base ATK 4 becomes 5 with Fury, 6 with Rage, or 7 with both. Aura retains its concrete adjacent-friendly DEF bonus representation and rules.


## Gambit (Resident player)

Session setup accepts exact `controllerAssignments` entries shaped `{typeId,sideId,controller}` for every authored Unit Type + Side on scenario, import and restart commands. `controller` is `Human`, `Default Behavior`, or `Gambit`; all Gambit entries must share one Side, while other Unit Types on that Side may use either other provider. The legacy `controllers` Side-to-provider dictionary remains available for all-Human and whole-Side setup. Gambit uses normal Automated engine agency; provider routing follows the request's complete Activation Token, including defender and door rolls. Start Next Round remains manual. Gambit sessions disable relevance auto-choice; only dice and genuinely sole legal alternatives (including optional decline) resolve without inference.

Run the host with server-held `PROJECTDELVE_GAMBIT_TOKEN` and configure the same bearer credential in Resident's ProjectDelve connector. The local browser never receives this credential. The browser has one **Show decision commentary** toggle, off by default, for all players' optional commentary synchronized with event playback. No owner token or separate owner view is needed. Resident receives only its allowlisted gameplay observation, never commentary from opponents or private strategic information. Every browser transport removes future World Deck order. The connector credential assumes the existing trusted local deployment; do not expose this host to untrusted networks without an access policy.

The authenticated `/api/controllers/gambit` contract is version 3:

- GET `/status`: session, binding/Side, authoritative status/completion, own objective and meaning, and the current request binding (or null). Polling is non-inference and refreshes readiness contact; a contact older than 15 seconds is unavailable.
- GET `/request`: immutable binding, own objective/meaning, status/completion, typed legal request, allowlisted player observation, committed rules events, and advisory bounded Default Behavior previews for Select Unit and current multi-choice Unit decisions. No commentary or future draw order appears here. Observations explicitly enumerate public board, Units and effective stats/uses, deduplicated Type descriptions, activation/follow-up context, unordered token contents, objectives/results and World Deck counts/observable effects. Every Unit carries relative `agency`: `Direct` for a token assigned to this Gambit binding, `Allied` for a friendly token assigned to another provider, or `Hostile` for another Side; the raw provider map is not exposed. Previews run only on copied state, stop before dice, another controller, activation completion, unexpected randomness or their step bound, and are advisory rather than committed events or plans. Delegation is host-only for the selected Unit's current activation; it does not change Type+Side ownership, legality, gameplay randomness or forced-choice progression, and fresh Default Behavior choices are made against authoritative state during execution.
- POST `/admit`: exact binding object, serialized with reset/completion invalidation, before inference.
- POST `/selection`: `{submissionId,binding,candidateKey,commentary,activationControl}`. `activationControl` is `Direct` or `Delegate`. Select Unit delegation keeps its legal Unit `candidateKey`; mid-activation delegation is available only at an existing multi-choice, non-dice Gambit boundary and uses a null `candidateKey` because Default Behavior recomputes the current and subsequent choices. Every response is bound to `sessionId,bindingId,sideId,decisionId,revision,attemptId,cursorStart,cursorEnd`; trusted Resident code supplies identities. Legal selection/commentary validation occurs before effects. No fallback choice is made.
- GET `/receipts/{submissionId}`: `queued`, `applied`, `rejected`, or `unknown`, with stable payload/ID deduplication. Reconciliation of old receipts never mutates the match. The most recent 1024 submissions are retained in memory; expired/missing/restarted receipts mean unknown, never proof of nonexecution.
- POST `/error`: `{binding,error}` pauses the live request with a fixed safe browser message. Caller text is not relayed. Browser commands `controller/retry` and `controller/cancel` invalidate prior attempts without choosing; Retry preserves the immutable observation and rotates only attempt identity. Restart rotates session identity and invalidates admitted/in-flight work.

Committed semantic history is separate from the bounded SSE playback journal. Polling/admission do not acknowledge events; successful selection acknowledges through the request's ending cursor, and subsequent resolution events appear next time. The active unacknowledged history cap is 16384 events and a complete observation cap is 8 MiB. Overflow pauses visibly and requires match restart; alternatives/events are never silently truncated. Host restart loses the in-memory match/receipt window and creates a new session. Existing persistent Resident instances need no interruption to run isolated fake-controller tests.

### Cell Markers and Banana Peel

Cell Markers are separate typed Board components, stored in scenario `cellMarkers` and runtime `GameState.CellMarkers`. `CellMarkerTypes.BananaPeel` has the stable content ID `banana-peel`. Placement requires passable Terrain without a Unit, Machine or another Cell Marker; loose Resources are allowed. Scenario creation reports invalid entries as `cellMarkers[index]`. Existing scenarios and saves without markers use an empty collection.

Ordinary Move paths may end on a Peel but cannot cross one. The shared movement search applies this restriction for all controllers and footprint Cells. Move and Resource transport finish atomically, then arrival emits `CellMarkerConsumed` followed by `PostureChanged` when needed. The existing Lying activation boundary ends an activating Unit's remaining opportunities. Run Rails checks arrival after each successful step and stops when the Locomotive is Lying. Swap resolves both figure relocations before their arrival consequences; Displace and summoning use the same arrival resolver. Staying in place and activation start are not arrivals. Current Telekinesis only changes Posture and does not relocate or consume a Peel.

The **Banana Peel** playtest has a manually controlled Barbarian in a top-row corridor and a Locomotive with a Peel on its second rail step. Neither Side has a victory Objective. The Level Designer provides Banana Peel and Remove Marker brushes for pointer/keyboard placement, plus removal in the selected Cell's properties. Markers survive JSON/DELVE1 import, export and Play design; authoritative validation checks legality.

| Canonical ID | Immutable source | Runtime | Format |
| --- | --- | --- | --- |
| `banana-peel` | `assets/source/markers/banana-peel.png` | `ProjectDelve.Web/wwwroot/art/markers/banana-peel.webp` | 128 × 128 transparent WebP |

The new original is 1254 × 1254 RGBA with genuine alpha (0–255), no embedded color profile, EXIF orientation or animation. The existing artwork pipeline preserves the whole square canvas and transparent margins, using sRGB, Lanczos, WebP quality 85/method 6 and lossless resized alpha verification. The catalog now has 48 source images: 46 direct exports and the two railway material components. Existing runtime WebPs retain their bytes after confirming identical decoded pixels on full re-export. All original SHA-256 hashes were verified unchanged.

`board-visuals.js` resolves marker art through the `cellMarker` catalog. Markers render centered at 62% of a Cell above Terrain/rail decoration and below Units, with Resources in their separate corner. They ignore pointer input and leave Cell highlights and choices intact. Classic uses a Banana glyph with the marker's accessible name; Enhanced uses the image after successful decoding and retains the glyph for missing or failed artwork. No rotation, terrain variant/hash or seed lifecycle changes were introduced. The full pipeline regenerates the [catalog sheet](assets/previews/artwork-pack-01.png) and [marker composition preview](assets/previews/cell-markers.png), including 60px and 100px Cells on Stone Floor, Grass and Gravel. Contact-sheet/DOM verification is not browser visual approval.

### Follow Route and Diamond Kart

`FollowRoute` Standing Orders store ordered `waypoints` rectangles (`x`, `y`, `w`, `h`), `mode` (`Once`, `Loop`, `PingPong`), `currentIndex` and `direction` on the Unit Type + Side group. Scenario `orders[].route`, runtime Orders, JSON/DELVE1, observations and playback snapshots carry the same shared progress. The Level Designer's group editor adds/removes ordered waypoint rectangles and edits traversal mode and initial progress. Validation rejects empty/off-board rectangles and invalid indices/directions. `AssignOrder(..., route: ...)` uses the existing Order assignment boundary.

Only an ordinary Move endpoint, including Stay, completes the current waypoint; one Move advances at most one area. Larger Units count any occupied destination Cell. Forced movement and post-Attack movement do not advance routes. Once clears at its final waypoint; Loop wraps; Ping-pong reverses without repeating endpoints. Automated movement uses shared terrain-route distance queries and authoritative legal Move candidates. `UnitBehavior.RouteLookahead` optionally ranks destinations inside the current area by distance to the next area. Planning ignores Banana Peels while actual paths still stop at them. Normal Actions remain available.

The Unique **Diamond Kart** has MOV 5 / RNG 0 / ATK 0 / DEF 3 / HP 4, cannot Attack, accepts one Diamond in shared storage and uses the reusable Asphalt ordinary-movement restriction. Workers supply it through normal Give Resource. **Place Banana Peel** spends its authored Diamond cost and places the marker atomically in a legal adjacent passable Cell; loose Resources are allowed and Asphalt is optional. `UnitBehavior.PreferBananaPeel` prefers Asphalt targets, then the greatest comparable route distance to the next waypoint, then deterministic Cell order. Humans retain every legal choice.

Select **Köra runt banan och kasta banan** (`diamond-kart-racing`) to inspect two autonomous opposing Karts on a broad Loop circuit. Central Lava renews Coal; Workers cross the circuit to outside owned Diamond Presses and carry Diamonds back to Karts. Chief Mechanics replenish Workers up to their existing Side limit. Each Kart begins with one Diamond so throwing is visible immediately. There are no victory objectives, rankings or rewards. The authored layout can be adjusted in the Level Designer.

| Canonical ID | Immutable source | Runtime | Format |
| --- | --- | --- | --- |
| `diamond-kart-type` | `assets/source/units/diamond-kart.png` | `ProjectDelve.Web/wwwroot/art/units/diamond-kart.webp` | 512 × 512 sRGB WebP |

The Kart's new 1254 × 1254 RGB original has no embedded profile, EXIF orientation or animation. The existing pipeline exports the entire square with Lanczos and quality 85/method 6, without cropping or altering the original. The catalog now covers 53 originals: 51 direct exports and two railway material sources. Full re-export/decoding checks confirmed all originals' SHA-256 hashes unchanged; existing exports with identical decoded pixels retain their previous bytes. The regenerated catalog preview includes the Kart at circular token scale. Classic, missing-image fallbacks, terrain variant/rotation hashes and seed lifecycle are preserved. Browser visual approval remains pending when no browser surface is connected.
