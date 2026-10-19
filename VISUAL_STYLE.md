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

The implemented catalog also supports explicit `rotation: true` on terrain base and object descriptors. Stone Floor, Grass, Water and Lava bases and Tree/Table objects opt into stable quarter turns using a separate `terrain-rotation-v1` hash domain and separate base/object salts. The existing `terrain-v1` variant algorithm and host seed lifecycle remain unchanged. Portraits, Edges, directional Rail, Machines and Resources do not opt in.

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

## Implementation boundary

The renderer uses existing authoritative state/projections and events. Assets, visual seeds, animation timing and panel styling are presentation responsibilities only. All game interactions should remain usable in Classic and Enhanced 2D views. Add test-owned fixtures for rendering logic and visually inspect real scenarios; do not freeze authored scenario content in tests.
