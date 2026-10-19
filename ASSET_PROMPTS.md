# Project Delve — Asset Generation Prompts

Status: reference prompts for generating visual assets for the **Enhanced 2D** client.

These prompts are intended as reusable starting points for generating artwork in a consistent style.
See also:

- `VISUAL_STYLE.md`
- `README.md`
- `AGENTS.md`

## Usage notes

- Prefer generating **square originals** for portraits.
- Do **not** generate complete tokens with frames, text, HP, or side colors baked in.
- Portrait assets should be usable inside the token layout defined in `VISUAL_STYLE.md`.
- Terrain textures should remain readable under overlays and unit tokens.
- For consistency, once a good portrait style is found, use approved earlier images as **visual references** for later ones.
- Original generated assets should be stored separately from runtime-optimized assets.

Suggested source storage:

```text
assets/source/
  units/
  terrain/


  Shared style block
Use this as the base style direction for character portraits:
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

For unit portraits: square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Unit Portrait Prompts
Barbarian
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a rugged fantasy Barbarian warrior portrait. Broad-shouldered and physically imposing. Weathered skin, long untamed dark hair, thick beard, determined expression. Wears worn leather armor with simple fur accents and a few metal details. The upper part of a large battle axe is visible beside one shoulder but must not obscure the face. Warm earthy browns, weathered steel, and muted crimson accents. Subtle dark stone atmospheric background. No blood, gore, active combat, or injuries.

Zombie
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a classic fantasy Zombie portrait in the same visual style as a premium board game asset set. Pale gray-green skin, sunken eyes, slightly crooked jaw, messy thinning hair, vacant unsettling expression. Wears ragged medieval clothing in muted brown and desaturated green fabric. Clearly undead but stylized and not excessively gruesome. Cool green-gray tones with warm reflected highlights. Subtle dark stone atmospheric background. No blood, gore, exposed organs, active combat, or injuries.

Worker
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a fantasy Worker portrait for a board game. A practical laborer character with a sturdy but unheroic look, slightly worn face, simple clothes, leather straps, and work gear. Carrying or wearing hints of tools or resource-handling equipment, such as a small hammer, tongs, satchel, rope, or utility belt, but keep the face clear and readable. Friendly but focused expression. Earthy colors, practical materials, muted browns, gray cloth, and small metal details. Background subdued and neutral.

Optional goblin-worker variant
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a goblin Worker portrait for a fantasy board game. Small wiry goblin laborer with clever eyes, rough green skin, pointed ears, and practical work clothes. Carries or wears simple tools or resource-handling gear such as a satchel, rope, utility belt, or small hammer, while keeping the face clearly visible. Expression alert, industrious, and slightly mischievous. Earthy muted colors and a subdued background.

Geomancer
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a Geomancer portrait for a fantasy board game. A mysterious spellcaster associated with stone, earth, and crystal magic. Calm and calculating expression. Wears layered robes with mineral and crystal motifs, earth-toned fabric, and subtle arcane ornamentation. Include visible glowing crystal or gemstone elements near the shoulders, collar, or staff, but keep the face unobstructed. Colors should emphasize stone gray, dusty brown, muted teal, and luminous crystal highlights. Background subdued and atmospheric.

Terrain Prompts
Stone Floor 1
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Medium gray stone slabs with subtle variation, worn edges, small cracks, and slight dirt in the seams. The texture should feel like a premium fantasy dungeon board tile: readable, clean, slightly stylized, and suitable for repeated tiling on a square grid.

Use rich but controlled colors, subtle brushwork, and moderate detail. Keep contrast low enough that units, highlights, and overlays remain easy to read on top of the tile.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.

Stone Floor 2
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Similar style to a classic dungeon floor, but with slightly larger stone blocks, more irregular seams, and a touch of warmer gray-brown variation. Subtle wear, small chips, and faint grime in the joints. It should still match the same overall board game style and work together with other stone floor variants.

Readable, clean, slightly stylized, and suitable for repeated tiling on a square grid. Keep the texture relatively subdued so figures and gameplay overlays remain clear.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.

Stone Floor 3
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Similar style to other dungeon floor variants, but with a slightly darker cooler gray tone, finer cracks, and a bit more age and weathering while remaining subtle. The tile should feel coherent with a premium fantasy board game and work alongside other stone floor variants without standing out too strongly.

Readable, clean, slightly stylized, and suitable for repeated tiling on a square grid. Keep contrast moderate and avoid visual clutter so units and highlights remain easy to read.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.