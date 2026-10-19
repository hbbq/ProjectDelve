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

## Asset-generation workflow (planned, not yet implemented)

1. Start with a **small coherent pilot set**: Stone Floor variants, Wall, Door, Barbarian and Zombie portraits.
2. Generate/source images to a shared visual brief: strictly top-down board textures and circular-token-ready character portraits; consistent illustration style, lighting direction, proportions, contrast and material finish.
3. Review the images together *in the actual board*, not just individually. Check at real Cell/token sizes and with Side colors, nameplates, HP and highlights applied.
4. Iterate the style before expanding to the remaining Unit, Resource, Machine and Terrain content.
5. Keep source/generation prompts or art-production notes separate from runtime assets where useful. Record provenance/licensing for any externally sourced art. Avoid baking rules text or protected third-party characters/brands into illustrations.

Art direction is intentionally flexible until the pilot assets have been integrated and visually reviewed. The current mockup is the reference for mood; actual shipped assets should be a consistent, original set.

## Implementation boundary

The renderer uses existing authoritative state/projections and events. Assets, visual seeds, animation timing and panel styling are presentation responsibilities only. All game interactions should remain usable in Classic and Enhanced 2D views. Add test-owned fixtures for rendering logic and visually inspect real scenarios; do not freeze authored scenario content in tests.
