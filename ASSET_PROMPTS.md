# Project Delve — Asset Generation Prompts

Reference prompts for Enhanced 2D artwork; not game rules. See [VISUAL_STYLE.md](VISUAL_STYLE.md) and [README.md](README.md).

## Workflow

Generate square originals without text, labels, borders, Side rings or HP. Save original portraits in `assets/source/units/` and terrain in `assets/source/terrain/`. Keep originals unchanged; use `tools/prepare-artwork.py` to prepare runtime assets. Larger-than-512px originals are fine. Review artwork inside the actual game at token size. For consistent new character artwork, attach an approved existing portrait as a visual style reference.

For **new portraits**, concatenate the shared prompt with the specific subject prompt. Earlier prompts below are preserved in full as originally used and do not need the shared prompt added again.

## Shared portrait style

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

For unit portraits: square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Unit Portrait Prompts
```

## First batch — original portrait prompts

### Barbarian

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a rugged fantasy Barbarian warrior portrait. Broad-shouldered and physically imposing. Weathered skin, long untamed dark hair, thick beard, determined expression. Wears worn leather armor with simple fur accents and a few metal details. The upper part of a large battle axe is visible beside one shoulder but must not obscure the face. Warm earthy browns, weathered steel, and muted crimson accents. Subtle dark stone atmospheric background. No blood, gore, active combat, or injuries.
```

### Zombie

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a classic fantasy Zombie portrait in the same visual style as a premium board game asset set. Pale gray-green skin, sunken eyes, slightly crooked jaw, messy thinning hair, vacant unsettling expression. Wears ragged medieval clothing in muted brown and desaturated green fabric. Clearly undead but stylized and not excessively gruesome. Cool green-gray tones with warm reflected highlights. Subtle dark stone atmospheric background. No blood, gore, exposed organs, active combat, or injuries.
```

### Worker

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a fantasy Worker portrait for a board game. A practical laborer character with a sturdy but unheroic look, slightly worn face, simple clothes, leather straps, and work gear. Carrying or wearing hints of tools or resource-handling equipment, such as a small hammer, tongs, satchel, rope, or utility belt, but keep the face clear and readable. Friendly but focused expression. Earthy colors, practical materials, muted browns, gray cloth, and small metal details. Background subdued and neutral.
```

### Optional goblin-worker variant

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a goblin Worker portrait for a fantasy board game. Small wiry goblin laborer with clever eyes, rough green skin, pointed ears, and practical work clothes. Carries or wears simple tools or resource-handling gear such as a satchel, rope, utility belt, or small hammer, while keeping the face clearly visible. Expression alert, industrious, and slightly mischievous. Earthy muted colors and a subdued background.
```

### Geomancer

```text
Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic.

Designed for a polished top-down tactical fantasy board game. Clean, readable, premium board-game presentation.

Square 1:1 composition, suitable for a circular token crop. Head and upper torso centered. Strong, recognizable silhouette. Face clearly visible. Keep all important details inside the central 75% of the image so the portrait works well when circularly cropped and displayed at small size.

Readable at small size. Soft directional lighting from the upper left. Simple subdued background. No text, letters, numbers, borders, token frames, faction-color rings, health markers, interface elements, logos, or watermarks.

Create a Geomancer portrait for a fantasy board game. A mysterious spellcaster associated with stone, earth, and crystal magic. Calm and calculating expression. Wears layered robes with mineral and crystal motifs, earth-toned fabric, and subtle arcane ornamentation. Include visible glowing crystal or gemstone elements near the shoulders, collar, or staff, but keep the face unobstructed. Colors should emphasize stone gray, dusty brown, muted teal, and luminous crystal highlights. Background subdued and atmospheric.
```

## Next batch — portrait subjects

Append each subject prompt below to the shared portrait style. Match the lighting, painterly texture, framing and restrained background of the approved portraits.

### Wizard

```text
Create a fantasy Wizard portrait. An older scholarly human spellcaster with thoughtful alert eyes, silver beard, weathered deep-blue robes and a practical pointed hat. A carved staff is visible near the shoulder but does not cover the face. Muted blues, warm amber accents and believable cloth texture. Face should remain clear and expressive at very small size; preserve generous circular crop margins. No text or UI.
```

### Rogue

```text
Create a fantasy Rogue portrait. A sharp-eyed agile human adventurer in a dark but readable hood and practical weathered leather, with a subtle mischievous half-smile. A small dagger hilt visible near the shoulder, not over the face. Charcoal, worn brown and desaturated blue-gray accents. Bright enough to distinguish eyes, nose and mouth at small size. No text or UI.
```

### Grunt

```text
Create a fantasy Grunt portrait, a common rank-and-file monster infantryman: rough broad face, coarse gray-brown skin, heavy brow, crooked teeth and simple dented iron armor. Looks tough but not elite. Distinct from small green Goblins and enormous Trolls. Earthy gray-browns and restrained steel highlights. Strong recognizable face and shoulders at small size. No blood, text or UI.
```

### Skeleton Archer

```text
Create a fantasy Skeleton Archer portrait. Clearly recognizable ivory skull and skeletal neck, dark empty eye sockets, weathered archer hood and remnants of medieval leather armor. Part of a wooden bow visible beside one shoulder without covering the skull. Bone ivory, worn brown wood and muted dark cloth. Legible as a skeleton archer when reduced to a tiny circular token. No gore, text or UI.
```

### Goblin

```text
Create a classic fantasy Goblin portrait. Wiry green-skinned creature with oversized pointed ears, narrow mischievous face, keen eyes and a crooked grin. Patchwork scavenged leather and a few metal pieces; a nimble troublemaker, not a Worker. Moss green, warm brown and muted brass. Fit ears and expression safely in the circular crop. No blood, text or UI.
```

### Troll

```text
Create a fantasy Troll portrait. Enormous hulking monster with thick rough stone-gray to mossy olive skin, heavy jaw, prominent brow, wide nose, coarse hair and a threatening but slightly dim expression. Crude hide and leather at the shoulders; the top of a huge wooden club may appear alongside one shoulder. Distinctly bulkier than a Grunt, clearly readable face, subtle upper-left lighting. No gore, text or UI.
```

## First batch — original terrain prompts

### Stone Floor 1

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Medium gray stone slabs with subtle variation, worn edges, small cracks, and slight dirt in the seams. The texture should feel like a premium fantasy dungeon board tile: readable, clean, slightly stylized, and suitable for repeated tiling on a square grid.

Use rich but controlled colors, subtle brushwork, and moderate detail. Keep contrast low enough that units, highlights, and overlays remain easy to read on top of the tile.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.
```

### Stone Floor 2

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Similar style to a classic dungeon floor, but with slightly larger stone blocks, more irregular seams, and a touch of warmer gray-brown variation. Subtle wear, small chips, and faint grime in the joints. It should still match the same overall board game style and work together with other stone floor variants.

Readable, clean, slightly stylized, and suitable for repeated tiling on a square grid. Keep the texture relatively subdued so figures and gameplay overlays remain clear.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.
```

### Stone Floor 3

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down stone floor tile for a polished tactical fantasy board game.

Create a seamless square stone floor texture viewed directly from above. Similar style to other dungeon floor variants, but with a slightly darker cooler gray tone, finer cracks, and a bit more age and weathering while remaining subtle. The tile should feel coherent with a premium fantasy board game and work alongside other stone floor variants without standing out too strongly.

Readable, clean, slightly stylized, and suitable for repeated tiling on a square grid. Keep contrast moderate and avoid visual clutter so units and highlights remain easy to read.

No perspective, no objects, no text, no borders, no interface elements, no watermarks.
```

## Edge assets — next art batch

Generate one **horizontal master asset** for each kind; rotate for vertical use. Keep the three assets in a consistent hand-painted premium tabletop style matching the approved stone floor. Strict overhead view, no perspective, no baked-in UI/text. Prefer transparent surroundings; keep object silhouettes readable at board scale. Original images may be large, but the pipeline should prepare a horizontal runtime export (approximately 512×64), with rotation supplying the vertical version. Check visual alignment with the actual Edge renderer before batch generation.

### Wall

```text
Premium fantasy tabletop board game edge asset. Hand-painted digital illustration in the same polished fantasy board game style as the existing stone floor textures.

Create a long, horizontal dungeon stone wall segment intended to lie precisely along the boundary between two square board cells. Strictly top-down overhead view, not an isometric wall or a landscape scene. Weathered gray masonry, restrained cracks, slight highlights and shadows showing physical thickness. Keep the segment centered, uniform across its length, with clean edges suitable for repeated placement and 90-degree rotation.

The wall must be legible at very small board scale. Use a transparent background outside the physical wall silhouette where appropriate. No figures, scenery, text, UI, symbols, borders, logos or watermarks.
```

### Closed Door

```text
Premium fantasy tabletop board game edge asset, matching the same painted stone-and-wood material language as the wall artwork.

Create a long horizontal CLOSED medieval dungeon door or gate lying along the boundary between two board cells, viewed strictly from directly overhead. Solid wooden planks with subtle wood grain, modest iron braces and a restrained stone threshold or jambs. The middle must visibly close the passage, unlike an open doorway. Keep the silhouette centered and narrow enough for an edge overlay, with clear transparent surroundings and no perspective.

The component should work at small size and when rotated 90 degrees. No characters, text, UI, symbols, logos or watermarks.
```

### Open Door

```text
Premium fantasy tabletop board game edge asset, matching the closed wooden door and the stone wall illustration in the same set.

Create a long horizontal OPEN medieval dungeon doorway along the boundary between two board cells, viewed strictly from directly overhead. Show a restrained stone threshold or short frame and wooden door leaves moved to the sides so the center is an unmistakably clear passage. The open central gap must stay transparent; do not fill it with opaque floor or dark painted background. Keep the edge component centered and suitable for 90-degree rotation.

Readable at small board scale, same lighting and material palette as the closed door. Transparent surroundings. No characters, text, UI, symbols, logos or watermarks.
```

**Integration note:** Open Door must read as passable and Closed Door as blocked. Keep their state legible independently of the precise artwork. Review transparency and repeat seams in the actual game before accepting the assets.

## Future prompt candidates

Coal, Diamond, Diamond Press and further Unit/Terrain art. New assets should match the same art direction. Originals remain separate from optimized `wwwroot/art/` files.
