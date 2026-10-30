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

## Campaigns and the shared scenario library

The browser opens on a campaign menu. **Test Scenarios** contains the original scenarios; **Scenarios** lists every saved scenario, including those outside a campaign. Choose any scenario to start a fresh game. Campaigns only order references: they carry no Units, resources, unlocks or progress between games. One live game is still shared by everyone using the host.

In **Designer**, **Save to library** creates a saved scenario or updates the scenario you opened. **Library & Export** opens saved scenarios, sets their cover art, and provides **Save as new** for an independent copy. Renaming keeps identity and campaign references intact. Saving validates the scenario; incomplete work can still be kept with **Save draft**. **Test scenario** always plays the current workspace without saving it. Return with **Designer** to continue editing; browser recovery retains the workspace, metadata and unfinished form inputs. DELVE1 import/export and draft files remain available.

Use **Create campaign** on the menu or **Edit** on a campaign to set its name, description and cover, add library scenarios, and move or remove references. Editing a scenario updates it in all campaigns. Deleting a scenario from its Details page leaves visible unavailable references, which the campaign editor can remove. **Import cover image** stores PNG/JPEG/WebP artwork through the image picker; manual root-relative/HTTPS URLs remain supported. Use spoiler-free illustrations: browsing never renders the scenario board or its hidden contents. Missing artwork has a decorative fallback.

The library combines bundled content with local overrides by stable ID. The original C# scenarios remain bundled; additional finished content lives in `ProjectDelve.Web/Content/Library/library.json`. Local data defaults to `ProjectDelve.Web/App_Data/library/`, which is excluded from Git and publishing. Configure `Library__Path` or `--Library:Path <directory>` to use a durable directory outside deployment files. Back up the entire local directory, including `covers/`. Source labels, explicit restore controls, deletion markers and change tokens protect local experiments while allowing unmodified bundled content to receive updates.

See [Local and bundled campaign content](CAMPAIGN_CONTENT.md) for migration, backup, cover storage and the reproducible `--promote-campaign` command. This is a trusted local/shared-host tool with the same unauthenticated authoring access as Designer. Library JSON is limited to 64 MB and scenario requests to approximately 2 MB; cover imports have a separate 5 MB limit.

Direct links include `/#home`, `/#scenarios`, `/#campaign/<id>`, `/#scenario/<id>`, `/#designer`, and `/#play` (`/#playtest` also works). Existing game and Designer API routes remain available. Scenario links open selection rather than replacing a running game on page load.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to play successive Rounds on a hard-coded 6x5 stone floor board with a Barbarian at `(4,2)` and two Grunts at `(1,2)` and `(1,3)`. The barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2; rows 0 and 4 provide open routes around its ends. Barbarian can open the door as a Free Action. The Console uses the same named definitions as the Web host.

Hero decisions use numbered engine-supplied choices. Choose the supplied Stay or End Turn option, or `q` to quit. An optional follow-up Move still uses `0` to stay. The authored Grunt group uses DefaultAutomatedProvider. Tokens draw automatically; the Barbarian controller confirms each owned dice pool through the numbered Roll Dice option. Positions, HP, deaths, and door state persist between Rounds. The host shows canonical movement paths and resulting rules events. There are no victory conditions.

## Loot, Items and Equipment

Choose **Loot & Equipment** in the playtest catalog for the six-card V1 slice. Move onto the paired chest markers to draw two cards, collect known objects with the supplied Free Action choices, and use the Barbarian's starting Goat Bell and Second Wind. The scenario has no victory Objective so combinations can be explored. New scenarios and generated dungeons default to Classic. Existing no-Loot files, drafts and built-in playtests retain an empty Custom deck until explicitly changed.

**Classic**, **Fun**, **Experimental** and **Far Out** are live central presets. Designer shows their card quantities and total; **Copy to Custom** captures only those quantities for independent editing. Custom never copies card rules. All four currently contain ten cards from the existing six-card catalog: Classic emphasizes supplies, Fun mixes all six, Experimental emphasizes Phase/standing/extra Actions, and Far Out contains bedsheets and goat bells. The latter two are honest starting compositions, not new mechanics or visual effects.

Scenario JSON stores `loot: { preset: "Fun" }` or `loot: { composition: [{ kind: "LuckyBoots", count: 2 }] }`. `composition: []` disables drops. Legacy repeated `cards` lists migrate to Custom quantities on export; missing/null Loot in legacy JSON remains an empty Custom deck. New exports explicitly identify their deck. Creating a new game resolves the current preset, while save/resume keeps the already-created finite draw/discard piles. Preset edits never refill an ongoing game.

Drop defaults are explicitly authored using `Loot(...)` in `UnitRoster.cs`: generally 1/6, stronger/specialist Units 2/6, Red Dragon 3/6. Goblins and Workers use 1/6 and Trolls 2/6 even when summoned; farming is permitted and bounded by the finite deck. The four Heroes can own Items and Equipment by default. Designer's **Unit Type default** selection inherits current content; an override of **0/6** explicitly disables drops. Sparse `lootUnitOverrides` store optional `dropSuccesses`, `canHaveItems` and `canHaveEquipment` without copying Unit Types. Existing full authored Unit Type overrides are preserved. Balance values remain initial playtest choices.

In Designer, edit the finite **Loot Deck** beside World Effects. The group editor's **Loot & ownership · Unit Type** panel configures drop successes and the two independent ownership capabilities. Cell properties edit unopened marker counts and known objects; Unit properties edit **Individual inventory**. These are ordinary scenario data and participate in validation, import/export, history, recovery and unsaved playtest transfers. A draft may temporarily contain too many markers, but validation rejects it before play or export.

For physical play, assemble the configured draw cards and keep them separate from starting inventories and known ground objects. Shuffle the finite deck, use a marker for each unopened reward, and reserve one remaining draw card per marker. Roll a defeated Unit's drop die only when unreserved supply remains. Draw one card at pickup and resolve it completely; never return drawn or consumed cards to the draw pile. Put equipment and unspent Items beside the individual bearer, using numbered figure markers and matching inventory areas for group members. On defeat, put those cards at the former anchor Cell. See `CONSTRAINTS.md` for footprint, simultaneous-arrival and pickup timing rules.

Equipment has independent typed definitions and stable Type IDs; physical objects retain separate Instance IDs. Canonical and scenario-local Equipment use one validated content model for modifiers, Phase, Open Door and the existing stand-up Action. See [Equipment content](EQUIPMENT_CONTENT.md) for C# and JSON authoring, migration and milestone boundaries. A Unit may own one instance of each Equipment Type; different Types' modifiers stack independently. Automatic duplicate rewards go to the ground, and optional pickup rejects duplicate Types.

`LootResolution.cs` integrates mandatory pickup with completed arrivals, ordinary HP restoration, defeat events and the existing consequence queue. Inventories are immutable per-Unit collections; shared Unit Types are never mutated by acquisition. Effects apply only to the bearer. Equipment Actions use ordinary Action opportunities and Posture; Second Wind consumes one Item and adds an activation-scoped Action. Identical Second Wind copies share the existing per-unique-Bonus-Action limit, including World modifiers.

The finite draw pile is sampled without replacement through injected `IRandomProvider.LootDrawIndex`, whose default uses only injected d6 rolls. Markers reserve quantity, not card identities. Discard records never replenish supply. Copies and serialized Engine state retain inventories, remaining cards and activation progress; browser observations strip draw identities and filter ground objects using event-time exploration. No client computes ownership or pickup legality.

The client groups chests and face-up cards by Cell, labels inventories and shows source-labeled content beside native abilities. Human draws pause for dismissal; automated draws continue after a short display. Card reveals remain available with movement animations disabled, and **Skip remaining effects** bypasses subsequent reveals. Illustrations are simple symbols; the catalog currently contains only the six test cards. Additional Immediate damage, Posture or movement cards are not authored in V1, and there is no general effect language, trading, equip/unequip, per-item charge system or campaign carryover. Automated Units use available object pickups, Bell targets and acquired door actions, and can consume Second Wind after ordinary Actions; they do not plan routes specifically to seek Loot.

## Playtest board views

Terrain uses a validated per-game catalog with stable Type IDs, declared passability and LOS properties, Unit-owned category restrictions, and optional typed Round Start resource production. Scenario-local JSON Terrain can be painted in Designer and played with existing or generic visual recipes. Open **Environmental Production - Three Sources** or **Local Terrain - Curiosity Room** under Test Scenarios for playable examples; see [Terrain content](TERRAIN_CONTENT.md) for canonical and JSON authoring, legacy Lava compatibility and milestone scope.

Playtest also offers **Three.js Flat 2D** alongside Classic and Enhanced. It shares
the existing session, observed state, decisions, playback and surrounding UI.
See [THREE_VIEWPORT.md](THREE_VIEWPORT.md) for dependency reproduction, viewport
architecture, camera/input, artwork reuse, FoW layering, diagnostics and tests.

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

### Optional Unit concepts and references

Production 3D miniatures use the manual GLB inbox at `assets/import/models/`.
See [the batch-import workflow](assets/import/models/README.md) for inspection,
reviewed provenance, deterministic preparation and automatic source preservation
and catalog registration. The approved catalog currently includes Zombie, Troll,
Goblin, Barbarian, Cleric, Hobby Knight and Worker. `THREE_VIEWPORT.md` records
dimensions, gameplay QA and mixed-scene measurements.

Any Unit Type may keep optional concept material under `assets/source/units/<unit>/`:
`concepts.md` may contain backstory, established visual identity, design thoughts,
speculative ideas and future possibilities, with no mandatory headings or schema.
Concept notes are not authoritative gameplay rules; established rules remain
governed by `CONSTRAINTS.md` and canonical definitions.

An optional `reference/` directory can contain character sheets, full-body artwork,
turnarounds and other references for 3D models, Standees, portraits and scenario
illustrations. Zombie's existing example uses
[`CONCEPT.md`](assets/source/units/zombie/CONCEPT.md) and
[`reference/`](assets/source/units/zombie/reference/); preserve these existing files.
This convention introduces no required concept files or validation for any Unit Type.

## Browser playtest host

Decision Commentary is optional presentation metadata on a selected legal decision. Both decision submissions and commands accept a trimmed `commentary` string of at most 280 characters. Streaming updates publish `kind: "commentary"` before that decision's rules events; legacy responses carry `commentaries` with `eventIndex` counting preceding rules events. Each entry identifies the decision, acting Unit, controller, Side and candidate key. Fresh baselines omit historical commentary. The host retains its enriched and key-only external selection callbacks for isolated adapter tests; the production Gambit broker uses the authenticated Resident polling contract described below. All browser transports present decision commentary from every player separately from gameplay events. Resident observations exclude commentary entirely. Automated flavor text uses fixed wording and consumes no gameplay randomness. Browser commentary starts hidden and follows event playback.

Run `dotnet run --project ProjectDelve.Web -- --urls http://localhost:5080`, then open [localhost:5080](http://localhost:5080).

For browser automation, screenshots and iterative Designer layout inspection with Codex, see [Browser visual inspection](BROWSER_INSPECTION.md).

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

### Delve Chess — Peasants and Rooks

Select **Delve Chess — Peasants and Rooks** (`delve-chess-peasants-rooks`) for a compact 12 × 7 playtest. Blue Royal Peasants and Castle Guards face Red Goblin Peasants and Tower Trolls, with multiple Units per group. Open lanes, a Wall corner, Windows and Closed Doors exercise straight movement, diagonal melee and blocked advances. A human-controlled neutral Rogue can open Doors without adding capabilities to the Chess roles. Both combat Sides use the existing Eliminate Enemies Objective; the neutral gatekeeper has No Objective. Change group controllers to compare human and automated play.

Royal Peasant and Goblin Peasant share MOV 1 / RNG 1 / ATK 2 / DEF 2 / HP 1 and Prefer Lowest Effective Defense for normal Attack target selection. Castle Guard and Tower Troll share MOV 16 / RNG 1 / ATK 3 / DEF 4 / HP 1, Straight Orthogonal Movement and Advance On Kill. These are reusable Unit capabilities/preferences, independent of Side or scenario. Separate ordinary Moves may change direction, including Moves gained with Mushroom Boost. Advance On Kill reuses the post-Attack Move resolver with MOV 2, only adjacent former Cells of actually defeated normal-melee targets, and physical passage checks; it ignores ordinary MOV and Movement Pattern. Goblin Move After Attack retains its existing behavior. The full chessboard and later pieces remain deferred.

| Original under `assets/source/units/` | Canonical catalog key | Runtime under `wwwroot/art/units/` |
| --- | --- | --- |
| `royal-peasant.png` | `royal-peasant-type` | `royal-peasant.webp` |
| `goblin-peasant.png` | `goblin-peasant-type` | `goblin-peasant.webp` |
| `castle-guard.png` | `castle-guard-type` | `castle-guard.webp` |
| `tower-troll.png` | `tower-troll-type` | `tower-troll.webp` |

The four new portraits are opaque 1254 × 1254 RGB PNG originals, without profiles, EXIF orientation or animation. The established pipeline exports 512 × 512 sRGB WebPs using Lanczos and quality 85/method 6, without cropping or altering originals. All 65 sources were decoded and their SHA-256 hashes verified unchanged; all 63 direct exports and the two railway components are registered. The updated [catalog preview](assets/previews/artwork-pack-01.png) includes 50px circular tokens: faces and pawn/rook silhouettes remain readable. Catalog/error fallbacks and Classic token presentation remain available. Existing WebP bytes were retained after confirming identical decoded pixels on full re-export; the pipeline also regenerated railway SVGs. Contact-sheet review and DOM tests do not constitute browser approval; no browser surface was connected for actual-board QA.

### Delve Chess — Phase 2: Bishops and Knights

Select **Delve Chess — Phase 2: Bishops and Knights** (`delve-chess-phase-2`) for a compact 12 × 10 playtest with both courts' Peasants, Rooks, Bishops and Knights. Phase 1 remains available. Blue groups are human-controlled, Red groups automated; change controllers to compare play. A neutral Rogue opens Doors. Existing Objectives apply.

Royal Bishop and Goblin Bishop share MOV 16 / RNG 1 / ATK 3 / DEF 4 / HP 1, Diagonal Corridor Movement and Advance On Kill. Each diagonal segment costs two ordinary orthogonal steps through either legal corner, using the existing top/left/right/bottom ordering when both work. Corners and intermediate diagonal Cells are transit; only the final destination resolves arrival. Ordinary Moves cannot turn or stop at corners. Advance On Kill can move orthogonally and change checkerboard color.

Royal Knight and Hobby Knight share MOV 3 / RNG 1 / ATK 3 / DEF 4 / HP 1 and L-shaped Movement. Each ordinary Move allows one leap for 3 MOV, ignoring intermediate Cells and Edges while checking normal landing legality and arrival. Less than 3 MOV permits only Stay; extra MOV never chains leaps. Knights retain normal melee, including diagonal adjacency, and have no Advance On Kill or posture reaction. Both roles use default combat Behavior and can belong to either Side. Extra ordinary Moves reuse their patterns. Shared movement-cost calculation counts a leap as 3 MOV without inventing intermediate playback Cells; ordinary pathfinding and attack geometry remain unchanged.

Manual playtest landmarks (zero-based coordinates):

- The Bishop at `(1,1)` has open diagonals. Its southeast segment to `(2,2)` must use the lower corner; the next segment to `(3,3)` has both routes blocked. The enemy at `(0,1)` offers an orthogonal Advance On Kill.
- The Bishop at `(10,2)` is trapped in a one-cell-wide straight lane. The Bishop at `(8,2)` faces a Window and Closed Door; the neutral Rogue at `(8,3)` can change the Door passage. An Open Door at `(7,3)` offers another corner route.
- The Knight at `(4,7)` can leap across Walls, Water and neighboring Units. `(5,5)` and `(6,6)` are occupied landing choices to inspect; `(8,5)` is an impassable landing for the Hobby Knight at `(9,7)`. Landing at `(6,8)` hits a Banana Peel. A second Peel at `(7,4)` exercises Bishop arrival/transit restrictions.

| Original under `assets/source/units/` | Canonical catalog key | Runtime under `wwwroot/art/units/` |
| --- | --- | --- |
| `royal-bishop.png` | `royal-bishop-type` | `royal-bishop.webp` |
| `goblin-bishop.png` | `goblin-bishop-type` | `goblin-bishop.webp` |
| `royal-knight.png` | `royal-knight-type` | `royal-knight.webp` |
| `hobby-knight.png` | `hobby-knight-type` | `hobby-knight.webp` |

These four supplied originals are opaque 1254 × 1254 RGB PNGs without profiles, EXIF orientation or animation. The existing pipeline exports uncropped 512 × 512 sRGB WebPs with Lanczos, quality 85/method 6. All 69 original SHA-256 hashes were verified unchanged; all 67 direct exports and two railway materials are registered, with no missing sources. Existing WebP bytes were retained after verifying identical decoded pixels on full re-export. The regenerated [catalog preview](assets/previews/artwork-pack-01.png) includes 50px circular crops: Bishop mitres and both Knight horse heads/riders remain recognizable. Artwork was not regenerated. Enhanced portrait/error fallbacks and Classic presentation remain covered by automated tests. No browser surface was connected for actual-board visual QA; preview inspection is not browser approval. Queen, King, the full chessboard and Knight Rear Up remain deferred.

### Delve Chess — Phase 3: Queens, Kings and Chess

Select **Chess** (`chess`) for the complete introductory 8 × 8 Stone Floor / Asphalt board. Each court deploys sixteen Units in classical file order, with Queen on file four and King on file five. Blue is human-controlled and Red automated; controllers can be changed normally. Each Side wins by actually defeating the opposing King group through the existing Defeat Units Objective. Normal Unit Type + Side tokens govern activation, including all eight Peasants activating sequentially. Both earlier Chess playtests remain available.

Royal Queen and Witch Queen are Unique per Side, with MOV 16 / RNG 1 / ATK 4 / DEF 5 / HP 3, Normal Attack, default combat Behavior and Advance On Kill. Their reusable Straight Orthogonal or Diagonal Corridor pattern composes the existing complete path searches: choose one mode per ordinary Move, retaining its costs, passage restrictions, deterministic corners and atomic arrival. Advance On Kill retains its own allowance and passage rules.

Royal King and Goblin King are Unique per Side, with MOV 2 / RNG 1 / ATK 2 / DEF 3 / HP 5, Normal Attack and default combat Behavior. Their reusable Adjacent pattern uses the same searches limited to one cardinal step or one two-step diagonal segment. Effective MOV still limits cost, and extra ordinary Moves independently apply the pattern. Their Aura directly reuses Cleric's adjacent-friendly +1 DEF component, including LOS, Upright activity and stacking. All four types can be assigned to arbitrary Sides and appear through the canonical registry in the Level Designer.

| Original under `assets/source/units/` | Canonical catalog key | Runtime under `wwwroot/art/units/` |
| --- | --- | --- |
| `royal-queen.png` | `royal-queen-type` | `royal-queen.webp` |
| `witch-queen.png` | `witch-queen-type` | `witch-queen.webp` |
| `royal-king.png` | `royal-king-type` | `royal-king.webp` |
| `goblin-king.png` | `goblin-king-type` | `goblin-king.webp` |

The four supplied opaque 1254 × 1254 RGB PNGs have no ICC profiles, EXIF orientation or animation. The established pipeline exports uncropped 512 × 512 sRGB WebPs using Lanczos, quality 85 / method 6. All 73 original hashes were verified unchanged; all 71 direct exports and both railway materials are registered, with no missing sources. Existing WebP bytes were retained after confirming identical decoded pixels on full re-export. The regenerated [catalog preview](assets/previews/artwork-pack-01.png) and new [court preview](assets/previews/chess-court.png) show circular crops at 50px and 84px: faces and crowns remain legible. Enhanced image/error fallbacks and Classic presentation remain covered by tests. No browser surface was connected for actual-board QA; preview review is not browser approval. Knight Rear Up and the Chess Room remain deferred.

### Scenario Designer — Phase 1

Click **Designer** in the persistent application navigation to author the same `ScenarioDefinition` format visually. The document bar provides New, Open draft, Save draft, Undo/Redo, Validate and Test scenario. The board stays beside a contextual inspector with Board, Groups, Sides & Objectives, Routes, and Library & Export sections. Zoom offers fit and larger Cell targets. The editor does not evaluate Delve placement, terrain, Unique, HP, or agency legality.

Drafts recover automatically after reload using browser-local storage, including invalid scenarios and unfinished form inputs. **Save draft** downloads a versioned `.delve-draft.json` editable document; **Open draft** restores it without requiring engine validity. This is separate from validated playable DELVE1 transport. Recovery status reports storage failures and incompatible/damaged data; unreadable recovery data can be downloaded before deliberate replacement. Another tab changing recovery pauses writes in this tab. Keep files for durable backups; browser recovery uses one slot per origin.

Inspector properties commit on change. Board, World Effects, Group and Route forms use explicit Apply/Save. Unfinished inputs remain visible when returning to their form and survive unrelated edits/reload. Status links return to unfinished forms; Validate, Export and Test wait until these inputs are applied or deliberately discarded. Rejected compound edits preserve both definition and revision. Paint strokes and compound edits undo atomically; new edits clear Redo. Selection and zoom do not enter history. Undo/Redo is bounded to 100 operations and lasts for the current page session, including transitions to Playtest and back; recovery starts a new history.

**Terrain** paints sparse overrides by click/drag; **Use default** removes an override. **Edge** paints internal Right/Down boundaries, including empty ones, with Walls, Windows and open/closed Doors; **Remove** erases a segment. **Unit** places the chosen canonical Type at its top-left anchor with arbitrary SideId and an explicit Type + Side controller. **Select** opens cell/edge/Unit properties. Overlapping Units remain editable through a chooser or the placement list. Unit properties include anchor, SideId, Posture and optional InitialHp; blank HP means canonical full HP.

Agency rows are per `(UnitTypeId, SideId)` and can be added, edited or removed independently. Existing pairs are reused on placement; rows with no initial Units are retained. First Type use appends its declaration, deletion does not remove it, imported Type order is preserved, and unplaced Types can be declared explicitly. Order is displayed; V1 does not reorder declarations.

**Validate** checks a snapshot through engine creation. **Export DELVE1** in Library & Export always validates before encoding. **Test scenario** exports, then uses the existing revisioned `/api/game/scenario/import` operation to create round-zero gameplay; click **Start round** to begin. Return through **Designer** to refine the retained draft. Runtime state never becomes authoring state. Errors leave the draft editable and offer location links where supplied by the engine; responses from older drafts/requests are ignored. Edits mark it **Unchecked**. No validation requests run during painting.

The Web host limits designer boards and imported playtest boards to **50 × 50** before materialization. This is host/editor policy, not an engine game rule. Shrinking is refused if it would hide authored terrain, edges or any Unit footprint. DefaultTerrain changes retain explicit overrides. Import retains the decoded definition directly, including redundant overrides, explicit None edges, unplaced Types, zero-placement agency, optional HP and all list order.

The designer uses stateless endpoints: `GET /api/designer/catalog`, `GET /api/designer/scenarios/{id}` for an editable catalog copy, `POST /api/designer/import` with `{ transport }`, and `POST /api/designer/validate` / `export` with the ScenarioDefinition JSON body. Catalog metadata comes from the engine's single `CanonicalUnitTypes` registry. Validation returns `{ valid, errors: [{ message, path }] }`; first-error reporting includes authoring locations where available. Import returns scenario JSON; export returns `{ transport }`. These operations never mutate the active game. Custom definitions are retained; Units absent from the catalog have an explicitly labeled anchor-only preview. Full custom-content geometry compatibility remains future work.

Browser tests exercise the actual editor modules using the small Node DOM harness:

```powershell
node --test ProjectDelve.Web.Tests/*.test.cjs
```

For browser acceptance use the isolated workflow in [BROWSER_INSPECTION.md](BROWSER_INSPECTION.md). Run `playwright-cli -s=<temporary-session> run-code --filename=ProjectDelve.Web.Tests/designer.browser.cjs` against the isolated host to exercise a dedicated test-owned scenario, stroke history, rejected edits, Route progress, recovery, file save/open, validation navigation, DELVE1 and Playtest return. The script replaces only the temporary browser draft and isolated host game; it saves screenshots/downloads under `.playwright-cli/`. Inspect the PNGs separately. Room/line tools, richer Objective editing, Resources/group inventory authoring, direct object manipulation and declaration reordering remain future work.

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

### Shared Waypoints, Routes and racing items

Scenario `waypoints` contain stable `id` and rectangular `area` (`x`, `y`, `w`, `h`). Scenario `routes` contain stable `id`, ordered `waypointIds` (including repeated references), and `mode` (`Once`, `Loop`, `PingPong`). Multiple Routes may reuse Waypoints; multiple groups may reuse a Route. `orders[].route` contains only `routeId`, `currentIndex` and `direction`. Runtime state, JSON/DELVE1, browser playback and Gambit observations retain these shared definitions and independent group progress. The former embedded-rectangle Order model is replaced; old route-bearing exports must be reauthored in the shared format.

The Level Designer has a **Shared Waypoints and Routes** panel. Create/edit rectangle identities and geometry, assemble Routes using existing Waypoint selectors in order, reorder/remove entries, choose traversal mode, then assign a Route in each Type + Side group editor. Renaming a Waypoint updates Route references. Saving or renaming a Route retains initial progress. Sequence edits retain a group's progress while its current index still targets the same Waypoint; changed current targets or traversal modes require an explicit reset for affected groups. Deleting a referenced Waypoint or assigned Route is refused until its references are removed. Board shrinking cannot hide Waypoints. The board shows subtle, non-interactive rectangle outlines; **Show Waypoints** toggles them in play. Classic retains labels, outlines and glyph fallbacks.

Only an ordinary Move endpoint, including Stay, reaches the current Waypoint; one Move advances at most one Waypoint. Larger Units count any occupied destination Cell. Forced movement and post-Attack movement do not advance Routes. Once clears at its final Waypoint, Loop wraps, and Ping-pong reverses without repeating endpoints. `WaypointReached` identifies the arriving Unit, group, Route and Waypoint; the existing RouteAdvanced/RouteCompleted events remain available. Route Lookahead uses shared terrain-route queries and the next Waypoint in the actual traversal sequence. Planning ignores Peels; actual movement still stops and slips on them.

Activations count completed Moves against effective Move opportunities, with a minimum of zero. All available Moves must finish before normal Actions or End Turn. Bonus Actions and Free Actions retain flexible timing. Boost adds a Move after spending its Mushroom, including after an earlier Move, but cannot be used after any normal Action. Each extra Move resolves normal movement, transport, arrival and Route advancement independently.

The canonical Unique **Diamond Kart** retains MOV 5 / RNG 0 / ATK 0 / DEF 3 / HP 4, CantAttack, Asphalt ordinary movement and Route Lookahead. Its shared storage accepts one each of **Banana**, **Mushroom** and **Shell**. Workers deliver through ordinary Give Resource. Diamond and Coal remain separate Resources; Banana Peel remains a Cell Marker.

- **Place Banana Peel (Bonus Action):** spend 1 Banana to place a marker in a legal adjacent passable Cell, including off Asphalt or on loose Resources. Automation waits until all Moves are complete, prefers Asphalt, then uses existing route-distance and deterministic Cell ranking.
- **Boost (Bonus Action):** spend 1 Mushroom for one extra Move this activation. Automation uses it immediately whenever legal, even without a useful destination.
- **Shoot Shell (Action):** spend 1 Shell to attack a legal hostile target with RNG 4 / ATK 3 through ordinary LOS, dice, defense and damage resolution. CantAttack does not suppress this explicit ability. Automation picks the nearest legal target, then top-left board order and stable key. Without a target it retains the Shell.
- **Waypoint Slot Machine (automatic):** at activation end, a Kart whose own ordinary Move advanced its group's Waypoint rolls once: d6 1–3 Banana, 4–5 Mushroom, 6 Shell. Store one if capacity allows; otherwise discard. Multiple advances still give one roll, and the item cannot be spent earlier in that activation.

**Fruit Tree** is impassable and blocks LOS like Tree. It shares Lava's two ordered directional d8 attempts and Round Start placement logic, producing **Fruit**. Per the confirmed rules clarification, generation ignores only the source Fruit Tree's own terrain blocker; other terrain, Walls, Doors and corner LOS rules remain in force. Ordinary LOS is unchanged. Generation occurs after World Cards and before the Activation Bag.

**Slot Machine** accepts 3 Fruit; Operate consumes all 3 and produces one item on the operator's Cell from the same Racing Slot Table. The Machine Operation model supports either a fixed Resource or an authored six-face Resource table. Ownership, transfer, output occupancy, Worker planning and atomic costs use the existing Machine system. No rerolls or substitutions occur. Diamond Press remains available.

### Focused racing playtests

- **Simple Racing** (`simple-racing`): two automated opposing Karts, a three-Cell-wide Asphalt ring, four shared rectangular Waypoints, independent group progress, and one starting Banana/Mushroom/Shell per Side. No industry or victory objective.
- **Racing and Industry** (`racing-and-industry`): the same circuit and shared Route, four inside Fruit Trees, outside Side-owned Slot Machines, Chief Mechanics and Workers. Inside starting Fruit and outside preloaded Machines expose both collection and item delivery early. Workers must cross Asphalt between orchard and workshop; ordinary behavior supplies all movement and delivery choices. No scripted paths or racing-specific Worker logic.

The earlier racing scenarios retain their authored layouts and now reference shared definitions. Their Diamond Press economies remain historical playtest content; use Racing and Industry for the Fruit-to-item economy.

Run repeatable autonomous diagnostics with:

```powershell
dotnet run --project tools/RacingSimulation --artifacts-path .artwork-qa -- 40 20261020
```

The optional arguments are rounds, seed, then scenario IDs. The tool counts authoritative events without changing game rules. Detailed mechanics use test-owned fixtures; authored scenario tests only validate loading/instantiation.

One seeded 40-round run of each scenario completed with no game completion:

| Metric | Simple Racing | Racing and Industry |
| --- | ---: | ---: |
| Waypoint advances | 39 | 40 |
| Waypoint rolls / stored awards | 39 / 37 | 40 / 38 |
| Boosts / Shell attacks | 14 / 4 | 19 / 12 |
| Peels / slips | 22 / 8 | 27 / 12 |
| Fruit created | 0 | 66 |
| Fruit deliveries to Machines / operations | 0 / 0 | 48 / 18 |
| Item deliveries to Karts | 0 | 15 |

These are diagnostics from seed 20261020, not balance guarantees. Shells can defeat participants; the scenarios intentionally add no special resurrection or racing victory system.

### Fruit Tree, item and Slot Machine artwork

All eight new originals are 1254 × 1254 RGBA PNGs with genuine transparency, no embedded color profiles, EXIF orientation or animation. The full existing pipeline exports uncropped sRGB WebPs using Lanczos, quality 85/method 6, and verifies exact resized alpha. The current source inventory has 61 originals: 59 direct exports plus two railway components. Original SHA-256 hashes are unchanged.

| Original under `assets/source/` | Catalog key | Runtime under `wwwroot/art/` | Size |
| --- | --- | --- | --- |
| `terrain/objects/fruit-tree-01.png` … `03.png` | `terrain.FruitTree` | `terrain/objects/fruit-tree/01.webp` … `03.webp` | 256 × 256 |
| `resources/fruit.png` | `resource.fruit` | `resources/fruit.webp` | 128 × 128 |
| `resources/banana.png` | `resource.banana` | `resources/banana.webp` | 128 × 128 |
| `resources/mushroom.png` | `resource.mushroom` | `resources/mushroom.webp` | 128 × 128 |
| `resources/shell.png` | `resource.shell` | `resources/shell.webp` | 128 × 128 |
| `machines/slot-machine.png` | `machine.slot-machine` | `machines/slot-machine.webp` | 256 × 256 |

Fruit Tree composes existing Grass with three independent deterministic object variants and quarter turns. Existing terrain hashes, variant counts/order and seed lifecycle are unchanged. Resource and Machine images retain transparent margins, glyph/error fallbacks and non-interactive decoration; ownership, storage and labels remain separate UI. The catalog, composite terrain and Machine/Resource contact sheets are generated by `tools/prepare-artwork.py`. No browser surface was connected for actual-board visual QA; previews and automated Classic/Enhanced DOM tests are not visual approval.

### Solid Rock artwork

Three new opaque 1254 × 1254 RGB PNG originals map to the existing `terrain.SolidRock` catalog entry. They have no embedded color profiles, EXIF orientation or animation. The existing pipeline exports the entire square without cropping, using sRGB, Lanczos and WebP quality 85/method 6.

| Original under `assets/source/terrain/` | Ordered runtime path under `wwwroot/art/` | Dimensions / bytes |
| --- | --- | --- |
| `solid-rock-01.png` | `terrain/solid-rock/01.webp` | 256 × 256 / 18,868 |
| `solid-rock-02.png` | `terrain/solid-rock/02.webp` | 256 × 256 / 18,712 |
| `solid-rock-03.png` | `terrain/solid-rock/03.webp` | 256 × 256 / 18,670 |

Solid Rock gains three deterministic image slots and opts into the existing independent quarter-turn rotation. Its distinct material remains beneath failed images, and Classic keeps its striped Rock presentation. Existing terrain slot counts/order, hash versions, seed derivation/lifecycle and gameplay RNG are unchanged. No renderer, CSS, rules or scenario changes are required.

The recursive inventory contains 76 originals: 74 direct bitmap exports and two railway material components. All sources have pipeline/catalog mappings or the documented railway material role; no files are blocked and no runtime assets lack sources. Full re-export reproduced all 71 existing WebPs' decoded pixels and color data (ICC creation dates excluded); their original bytes and the four equivalent railway SVGs were retained. All original SHA-256 hashes remained unchanged.

The regenerated [catalog sheet](assets/previews/artwork-pack-01.png) includes Solid Rock. The new [tiled preview](assets/previews/solid-rock.png) repeats each slot in a 3 × 3 block at 60px and 100px Cell sizes, without an added grid. Edge browser inspection on an isolated temporary board checked normal and approximately 60px Cells, Stone Floor contrast, both Door orientations and Open Door gaps, tokens/Side/name/HP/Posture, legal-choice feedback, reload, Classic, and Fog of War. Failed-image inspection used a disposable copy of the web root and confirmed the Rock material and portrait glyph fallbacks. Normal inspections had no console or request errors; the failure check produced the four deliberate 404s. Keyboard interaction and Door playback retain automated coverage. The existing port-5080 game was preserved.
