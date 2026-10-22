# Project Delve — Visual Style

Status: **visual direction and implementation guidance**, not game rules. This document describes the intended **Enhanced 2D** presentation. `CONSTRAINTS.md` remains authoritative for gameplay; visual styling must not change rules or legal choices.

## Direction: premium top-down tabletop

- Retain a square, easily readable top-down grid and the feel of a physical board game.
- Aim for a polished illustrated fantasy tabletop: subtly varied stone/grass terrain, tactile walls and doors, illustrated circular unit tokens, restrained shadows, warm accents, and legible UI panels.
- Keep game information dominant over decoration: Unit position, Side, Posture, Cell/Edge boundaries, legal options, current activation, and Resource visibility must remain obvious.
- The style must be extensible to industrial machinery, modern racing and railways without becoming visually incoherent.
- Preserve **Classic View** as an independent, usable presentation option. Visual upgrades target **Enhanced 2D**.
- Use the approved visual concept from the project discussion as an *art-direction reference*, not as a literal screenshot to reproduce or as a source of new game rules.

## Unit tokens

- Circular token with a clearly visible outer ring in the Unit's current **Side color** (not intrinsically blue/red). Slight depth/bevel/shadow is welcome.
- Central illustrated **portrait** representing the Unit Type, not a mechanically meaningful 3D/facing model. A missing image falls back gracefully to an icon or existing label.
- **Bottom nameplate:** rounded rectangle, contrasting background, readable at normal board scale. Prefer the explicitly authored individual display name when available; otherwise use the Unit Type display name. Do not treat an internal generated Unit ID as a name unless the presentation explicitly defines it as one.
- **Top HP badge:** `current/max`, only if maximum HP is greater than one. Do not show unnecessary `1/1` badges.
- Show Side, selected, hover, active, and Lying/Upright states clearly and independently. Do not rely on color alone. Keep name and HP readable in Lying state.
- Tokens fit inside their owning Cell without hiding the square grid or adjacent important markers. Portrait, name and HP are composited by the UI rather than baked into the portrait asset.

## Board materials and objects

- Maintain visible Cell and Edge boundaries. Terrain art should be relatively low contrast compared to figures and interactive highlights.
- First terrain candidates: Stone Floor (3 subtle variants), Grass, Tree, Water, Lava and directional Rail.
- First Edge candidates: Wall, Closed Door, Open Door. Show their rule-relevant states unambiguously.
- First Resource candidates: Coal and Diamond. First Machine: Diamond Press.
- Tokens, board Resources and overlays must remain distinguishable when sharing a Cell. Machine appearance and owner indicators must remain legible.
- Effects/highlights for legal Moves, selected Units, targets and ongoing events must not be obscured by decorative layers.

## UI direction

- Dark, restrained framing with warm parchment/gold accents; clear visual hierarchy and accessible contrast.
- Eventually polish Round Start, active Side + Unit Type, Activation Bag, Unit/Machine cards, choice feedback and the event log.
- Keep authoring/designer and gameplay interfaces functionally distinct; this file does not request a redesign of the Level Designer.

## Asset catalog and layout

Store image assets as **presentation-only files** and resolve them through stable content IDs in the existing visual catalog, not through rules data or scattered hard-coded file paths.

Suggested folders (illustrative; adapt to the current catalog):

```text
ProjectDelve.Web/wwwroot/art/
  terrain/stone-floor/01.webp, 02.webp, 03.webp
  terrain/grass/01.webp
  terrain/water/01.webp
  terrain/lava/01.webp
  terrain/rail/north.webp, east.webp, south.webp, west.webp
  edges/wall.webp, door-closed.webp, door-open.webp
  resources/coal.webp, diamond.webp
  machines/diamond-press.webp
  units/barbarian.webp, wizard.webp, rogue.webp, zombie.webp
  units/goblin.webp, skeleton-archer.webp, troll.webp
  units/worker.webp, geomancer.webp, locomotive.webp
```

These names are examples, not a demand to rename current content identifiers. Prefer WebP or PNG depending on alpha/transparency and actual quality. Use images without embedded text, frames, Side colors, nameplates or HP so the client can compose these dynamically. Preserve fallbacks for unillustrated content and newly authored scenario Unit Types.

## Deterministic terrain variants

- A particular scenario should produce a stable visual variant for each Cell across refresh, replay and UI rerenders; no fresh random choice during rendering.
- Choose variant by a stable algorithm based on **scenario-scoped visual seed + terrain content ID + x + y**, modulo variant count.
- A future optional explicit `VisualSeed` in **presentation metadata** may override the derived seed; do not make it a gameplay rule.
- If deriving a seed from the full scenario definition, use a *canonical, stable* serialization rather than incidental property order or runtime hashes. **Caution:** any scenario edit could reroll variants for the entire map. An explicit, persisted visual seed is preferable once this becomes inconvenient.
- The variant selection algorithm and hash must be stable across browsers and runtime versions. Avoid nondeterministic JavaScript hashes or `Math.random()`.
- The visual seed must not change the engine's RNG, rules, or authoritative scenario state.

The implemented catalog also supports explicit `rotation: true` on terrain base and object descriptors. Stone Floor, Grass, Water, Lava and Gravel bases (including Rail's Gravel base) and Tree/Table objects opt into stable quarter turns using a separate `terrain-rotation-v1` hash domain and separate base/object salts. The existing `terrain-v1` variant algorithm and host seed lifecycle remain unchanged. Portraits, Edges, Rail track geometry/arrows, Machines and Resources do not opt in.

Tree composes the existing Grass texture catalog with a transparent Tree object; Stone Floor with Table composes the existing Stone Floor catalog with a transparent Table object. Each object has three independently selected variants. Centered decoration rotates within the square Cell while geometry and hit targets stay fixed. Failed object loads retain the previous material silhouette; transparent pixels in successfully loaded images reveal the base. Units, Resources and feedback remain above decoration. Classic and the Level Designer retain their existing rendering. See README for source/export mappings and exact hash inputs.

## Asset-generation workflow (planned, not yet implemented)

1. Start with a **small coherent pilot set**: Stone Floor variants, Wall, Door, Barbarian and Zombie portraits.
2. Generate/source images to a shared visual brief: strictly top-down board textures and circular-token-ready character portraits; consistent illustration style, lighting direction, proportions, contrast and material finish.
3. Review the images together *in the actual board*, not just individually. Check at real Cell/token sizes and with Side colors, nameplates, HP and highlights applied.
4. Iterate the style before expanding to the remaining Unit, Resource, Machine and Terrain content.
5. Keep source/generation prompts or art-production notes separate from runtime assets where useful. Record provenance/licensing for any externally sourced art. Avoid baking rules text or protected third-party characters/brands into illustrations.

Art direction is intentionally flexible until the pilot assets have been integrated and visually reviewed. The current mockup is the reference for mood; actual shipped assets should be a consistent, original set.

## Directional rail presentation

Rail's **outgoing direction** is authoritative gameplay/scenario data. Enhanced 2D derives a **visual connection mask** from the outgoing edge plus every orthogonally adjacent Rail Cell whose outgoing direction points into this Cell. A neighboring Rail Cell counts only when its outgoing direction points toward the current Cell; proximity alone does not connect it.

If **no** neighboring Rail Cell points in, add a *visual-only* incoming connection on the edge opposite the outgoing direction. This fallback changes no movement or rules.

If reciprocal outgoing arrows leave only one distinct connected side, also add the opposite visual connection and show a straight track. The outgoing arrow retains its authoritative direction.

Render two independent layers:
- **Track geometry:** choose one of four orientation-free base shapes based on the resulting set of connected sides: straight (two opposite sides), curve (two adjacent sides), T-junction (three sides), cross (four sides). Rotate the base shape by 0/90/180/270 degrees to align its exits. There is no need for separate 2-in/1-out track artwork: both are represented by the same undirected T-junction.
- **Direction arrow:** a separate, clear but unobtrusive overlay pointing toward the Cell's outgoing direction. Do not bake arrows into the track artwork.

The connection mask is derived afresh from current board geometry and outgoing directions, so a change to one Rail Cell can change its neighbors' visual shapes. Never use random rotation for Rail, and do not change Run Rails, game rules, or authoritative state. Use the existing board/terrain presentation layering and fallbacks.

### Gravel terrain and rail composition

**Gravel** is a standalone, ordinary terrain type with the same passability and LOS behavior as Stone Floor (no special gameplay effect). Author it in the canonical terrain model and Level Designer, with validation/serialization and existing terrain coverage as appropriate. It uses three selectable, deterministically distributed, independently rotated, opaque gravel artwork variants, like Grass and Stone Floor.

**Rail** is visually composed from the same Gravel base, plus a track-geometry overlay and a separate outgoing-direction arrow. Rail retains its existing directional gameplay behavior; Gravel does not acquire any railway rules. The visual catalog should reuse the Gravel material assets rather than duplicating them for Rail.

### Procedural rail geometry and artwork

The four track shapes **must share exactly matching edge ports**: identical track gauge, rail widths, sleeper scale, material palette, centerline placement, and entry/exit geometry at the middle of each connected Cell edge. Rotation must preserve seamless joins. Avoid perspective, baked-in floor/background, directional arrows, and asymmetrical edge-end decorations.

Generate/source a small material kit: **Gravel** (three opaque square variants), **one wooden sleeper** (transparent background) and **one steel rail material/reference**. Use shared, exact geometric parameters to assemble straight, curve, T-junction and cross from sleepers and two continuous rails. Draw/bend rail geometry with SVG/vector paths or generate transparent 256×256 shape overlays offline; do **not** stretch a straight rail PNG into curves or rely on AI-generated track-piece geometry for exact joins. Prefer pre-generated shape overlays from a reproducible script if practical, while keeping Gravel and the direction arrow separate at runtime.

Acceptance check: arrange straight, curve, T and cross pieces in a sample track network with rotations, verify all connected boundaries visually at actual board scale, and verify the separate arrows agree with outgoing directions. If ports do not match, fix the shared geometry instead of patching each image by eye.

## Playtest layout — board-first interaction (next UI iteration)

**Goal:** Make Playtest feel like a game rather than a development dashboard. Maximize the usable board area, reduce permanent interface chrome and preserve every existing legal choice and debug capability. This section governs presentation only; it does not change game rules, available decisions, resolution or the authoritative state.

### Persistent desktop layout

- The **board is the dominant surface**. Allocate it all practical space after reserving a compact right-side Unit Card and small status/control chrome. Preserve square Cells and map aspect ratio; do not stretch or crop the board to fill arbitrary rectangles. Handle large and small boards and narrow viewports gracefully; avoid gratuitous fixed board dimensions.
- A **compact always-visible status strip** shows current Round, currently activating Unit Type/Side or Unit when relevant, and **number of Activation Bag tokens remaining**. Detailed bag contents should be available on hover/focus/click (tooltip/popover) rather than taking permanent space. Never imply the future draw order is known; display the actual remaining membership/count, not a fabricated shuffled prediction.
- Keep the **Unit Card permanently visible on desktop** in the right column. Retain existing behavior: show the active Unit's card by default; temporarily show a hovered Unit's card while hovered, then return to the active Unit. Preserve existing clickable legal/relevant Action buttons, choice filtering and usage indicators. Do not redesign the card wholesale in this iteration.
- **End Turn / equivalent end-activation control has a stable, reserved position** and remains rendered, disabled whenever not currently a legal choice. Avoid layout shifts when choices appear or disappear. Bind it to existing legal decisions; do not invent an End Turn action where the engine does not provide one.
- On narrower screens, adapt layout without obscuring the board or losing access to the Unit Card and decisions; a compact card region or accessible panel is acceptable.

### Contextual choices and dice

- Put **Roll Dice** and other transient decisions that cannot be expressed by clicking a board Cell/Unit or existing Unit Card button in a small **contextual overlay over the board**, preferably near a noncritical edge. Examples: multiple distinct attacks on the same target, choice-specific confirmations and dice rolls.
- Do not duplicate existing board-click and card-button interactions unnecessarily. Every decision type must remain discoverable and selectable, including keyboard/focus accessibility. Preserve the existing decision-filtering semantics and manual/automatic choice modes.
- Show dice being rolled and recent dice outcomes briefly over the board, without permanently occupying a sidebar. Results must remain accessible afterward in Event History; avoid blocking board input after the roll resolves. Honor existing event-animation preferences, including disabled/reduced-motion behavior.
- Overlay placement should avoid covering the active/target Unit when practical, remain readable and clickable, and not cause the board to resize or jump. Multiple-choice and attack/defense phases must be handled without losing the current pending decision.

### Secondary tools, still available

- Move the bulky top/bottom controls into a **collapsible tools/sidebar area**, closed by default in normal play: Game Setup, scenario start/restart, Playtest/Designer navigation where appropriate, Board View selection, board legend, Objectives & Orders, Event History, optional decision commentary, display/animation/filter toggles, coordinates, Waypoints and developer/debug controls.
- Preserve all existing functionality and discoverability; don't delete controls merely to make the screen cleaner. A compact menu/tool button remains visible. Ensure that opening tools does not silently reset or interrupt a game.
- Objectives/Orders must remain quickly accessible even when collapsed, but need not consume permanent board width.
- Avoid permanently showing verbose IDs, dice logs, full Activation Bag token lists or long rules text in the main play area.

### Compact Unit Card ability text

- **Keep current Unit Card interaction and structure** for now: active/hover switching, legal choice buttons, relevance filter, resource/usage indicators and stats.
- Each Capability/Action may have a concise **short description** for permanent card display, and retain the existing **full description** available via tooltip plus a focus/click-accessible disclosure (not hover-only). A short description should explain what the ability *does*, not restate every exception. Full descriptions remain accurate and available.
- Prefer **one source of presentation metadata** attached to the existing capability/action definitions or shared projections; do not hardcode a second competing dictionary in the web client. Reuse the full description already in the model where possible. If short text is absent, provide a sensible nonempty fallback (e.g. title plus an accessible full-description disclosure), rather than clipping rules text silently.
- Short and long descriptions are **presentation**, never alternate rule definitions. Do not change legality, parameterization, effect timing or authoritative `CONSTRAINTS.md` to shorten card copy.
- Ensure that tooltips and disclosure controls work for mouse, keyboard and touch. Keep the card legible at normal desktop width; a more ambitious card redesign is explicitly a **later iteration**.

### Verification / acceptance

1. On a typical desktop Playtest screen, the board is visibly larger and dominates the viewport; secondary panels are initially collapsed.
2. The status strip shows accurate current activation and remaining bag count; full bag membership is accessible without exposing nonexistent future draw order.
3. The active/hover Unit Card, existing legal Action buttons, relevance filtering and usage marks behave exactly as before.
4. End Turn has a stable location and is disabled when not legal; no jumping control positions.
5. Dice decisions and ambiguous choices can be completed via contextual board overlays; results are briefly visible and remain in history.
6. Game Setup, Objectives/Orders, designer navigation, legend, settings and debugging remain accessible.
7. Classic View, Enhanced 2D, autoplay/auto-choice, replay and event animations remain functional. Add relevant UI tests and inspect a real 8×8 Chess board plus a larger scenario at desktop and narrow widths.

## Playtest UI polish — compact card and persistent combat dice

Follow-up to the board-first layout. These are **presentation-only** requirements; keep authoritative combat timing, dice, legal decisions and Unit Card interactions unchanged.

### Right column sizing and controls

- Place **End Turn** and **Next Round** together in a fixed row **above** the Unit Card. Keep both controls mounted and their positions stable as legal decisions change; use existing enable/disable semantics only.
- The Unit Card remains permanently available on desktop but **sizes vertically to its actual content**, instead of stretching to fill the whole board height. Do not leave a tall empty card background below its text.
- Preserve active-unit default, temporary hover-card replacement, relevant Action buttons, usage marks, stats and short/full descriptions. Switching hovered Units may change the card's height without shifting the fixed button row.
- Do not reduce board area or force the card to match the board's height. Keep narrow viewport access functional.

### One coherent attack presentation

- Treat Attack dice and Defence dice as **one visual combat sequence**, even if they require separate decisions, event updates or controllers.
- After Attack dice are resolved, **retain their actual faces/results visibly** throughout the pending Defence choice/roll. Do **not** expire them on a short animation timer while the attack remains unresolved.
- When Defence dice resolve, show both Attack and Defence results **together**, with their actual hit/block totals and any resolved consequence. Then allow the combined result to disappear after a short readable delay (approximately 1.5–2 seconds), consistent with animation preferences.
- Use only authoritative dice events/results and the existing attack/correlation identifiers or event sequence. Do not roll in the client, infer unreported dice, or invent a new gameplay combat phase. Handle automatic Attack/Defence, manual rolls, rapid successive attacks and interrupted/aborted sequences without mixing results from different attacks or leaving stale overlays.
- Preserve the movable contextual decision overlay and its existing positioning behavior. Ensure it stays usable while the previous Attack result is pinned, including at small viewport sizes. When animations are disabled or reduced, avoid forced delays; still present relevant dice while a Defence decision is pending. Full outcomes remain available in Event History.

### Human-readable names in normal play

- Replace internal IDs such as `scenario-unit-1` in player-facing contextual prompts, decision headings, and compact under-board status with **the Unit's authored display name, or Unit Type display name as fallback**.
- Prefer e.g. `Castle Guard attacks Goblin Peasant` and `Roll 3 Attack dice` rather than raw IDs. When several same-name Units must be distinguished, use a short, meaningful disambiguator (e.g. coordinates or an existing label), not a raw generated ID.
- Keep IDs intact in authoritative events, payloads, selection values, logs and expanded developer/debug views. This is display formatting only, not event rewriting.
- Keep attack and defence actor/target labels consistent across the full combat sequence.

### Acceptance checks

1. Right-side End Turn / Next Round controls are above a content-height Unit Card; no large empty panel or shifting controls.
2. Attack dice remain visible until Defence resolves, regardless of how long the user waits before rolling.
3. Both dice sets and totals are briefly visible together after resolution, then clear correctly; subsequent attacks do not inherit stale dice.
4. Automatic/manual rolls, animation-off/reduced-motion, hover/card switching, movable overlays and small screens still work.
5. Ordinary play uses readable Unit names; debug retains stable internal IDs.
6. All previous board-first UI and gameplay decision tests remain passing.

## Unit Card redesign — iteration 1

This initial visual iteration supersedes the earlier instruction to retain the card structure. It affects presentation only; preserve authoritative rules, decision generation and the existing visual theme.

- Lead with a square Unit Type portrait, approximately 100–120 CSS pixels on desktop and smaller where needed. Reuse the existing artwork catalog, show the illustration without the circular token crop, and provide a graceful missing-art fallback. Open a larger image in a modal viewer on click/touch/keyboard activation, with a visible close control, Escape dismissal and focus restoration. Do not alter original artwork.
- Group the display name, Side, current/max HP and compact MOV/RNG/ATK/DEF near the portrait. Show effective changes such as `DEF 2 → 3` and relevant changing status. Omit the redundant Active Unit heading and default Footprint 1×1; retain nonstandard footprints. Put internal IDs and verbose details in Tools/debug.
- Always list every ability, even when unavailable. Show its name, a discreet Action / Bonus Action / Free Action / Capability label, existing usage marks and a useful short description. Use the existing short/full metadata, including intentionally empty short descriptions for obvious abilities; do not create a client rule catalog.
- Replace repeated “Full description · Ability Name” lines with a small chevron or information disclosure. Reveal complete text inline using a keyboard- and touch-accessible control, and retain tooltip access. Keep disclosure activation separate from legal Action buttons, placed with their corresponding abilities; preserve disabled states, relevance filtering and accurate counters.
- Preserve active-card default and temporary hover switching, board selection, Classic and Enhanced 2D, stable End Turn / Next Round controls above the card, and board dimensions. Keep the card content-height with scrolling only when needed, rather than filling an empty panel. Adapt portrait, stats and abilities to narrow screens without horizontal overflow.
- Use the existing restrained fantasy tabletop palette, clear hierarchy, subtle separators, consistent small labels and comfortable compact spacing. Favor a maintainable first iteration over a new theme or elaborate effects.
- Verify portraits/fallbacks, enlargement/dismissal, disclosures and short/full text, unavailable abilities, legal buttons/usage, effective stats, hover switching, debug access, stable controls/board sizing and responsive behavior. Run relevant JavaScript/.NET tests and solution build. When browser QA is available, inspect Wizard, Diamond Kart or Chief Mechanic, and Peasant at desktop and narrow widths; otherwise report the limitation.

## Implementation boundary

The renderer uses existing authoritative state/projections and events. Assets, visual seeds, animation timing and panel styling are presentation responsibilities only. All game interactions should remain usable in Classic and Enhanced 2D views. Add test-owned fixtures for rendering logic and visually inspect real scenarios; do not freeze authored scenario content in tests.
