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

## Remaining canonical Unit Type portraits

The following subjects complete the currently canonical Unit Roster (beyond the portraits already covered above). For each, combine the **Shared portrait style** block with the subject prompt. For Locomotive, follow its special camera guidance while keeping consistent painting and lighting. Generate multiple candidates and select one that reads clearly in the circular token at small size.

### Cleric

Suggested source file: `assets/source/units/cleric.png`

```text
Create a fantasy Cleric portrait, a compassionate but battle-hardened human healer and priest in practical ivory robes and worn steel-and-brass shoulder armor. A modest sun-shaped religious medallion and a simple healing staff are visible but do not cover the face. Warm, calm expression, clear eyes, muted ivory, burgundy and antique gold. Not a Wizard, not excessively radiant; readable facial silhouette for a small circular tabletop token. No legible writing or symbols resembling text.
```

### Bomb Imp

Suggested source file: `assets/source/units/bomb imp.png`

```text
Create a mischievous fantasy Bomb Imp portrait, a small impish creature with pointed ears, tiny swept-back horns, soot-dark reddish skin and an excited, reckless grin. Its shoulder and one small smoking alchemical bomb are hinted at near the edge of the image, not covering the face. Distinct from the green Goblin. Warm ember orange, dark maroon and charcoal accents, painterly rather than explosive spectacle; a clear expressive face at small token size. No flames covering the subject, blood or text.
```

### Red Dragon

Suggested source file: `assets/source/units/red dragon.png`

```text
Create a red fantasy Dragon portrait for a tabletop board game. A majestic but threatening dragon head and upper neck, rich crimson scales, two dark swept-back horns, amber eyes and restrained gold highlights. A broad recognizable silhouette that fits a circular crop; the face points slightly toward the viewer, with horns safely within crop margins. Show detailed scales without excessive visual noise. No full-body scene, fire breath obscuring the head, text or UI.
```

### Labyrinth Shaper

Suggested source file: `assets/source/units/labyrinth shaper.png`

```text
Create a mysterious fantasy Labyrinth Shaper portrait: an eccentric ancient arcane architect who bends dungeon stone to their will. Humanoid figure with an enigmatic masked or deeply hooded face, weathered geometric stone armor and subtly floating masonry fragments around the shoulders. A distinctive face area remains readable, with slate, sandstone, deep teal and muted gold tones. Hint at maze-like forms in carved material, but no actual writing or maze diagram, no elaborate background, and no UI.
```

### Displacer Demon

Suggested source file: `assets/source/units/displacer demon.png`

```text
Create a fantasy Displacer Demon portrait: an unsettling supernatural demon with curved dark horns, angular face, glowing pale-violet eyes and a slightly distorted shifting silhouette, as though it can teleport or exchange places. Deep purple-black skin, restrained silver-gray accents, subtle violet atmospheric glow. Keep the face crisp and the displacement effect only at outer edges so the portrait remains readable at small size. No gore, text or dramatic full-body battle scene.
```

### Ghost

Suggested source file: `assets/source/units/ghost.png`

```text
Create a fantasy Ghost portrait: spectral humanoid face and shoulders emerging from translucent pale blue-gray mist, haunting but not gruesome. An antique hood or remnants of old robes frame a clearly recognizable sorrowful face with softly luminous eyes. Restrained icy cyan, desaturated silver and smoke gray, with darker background contrast so the spirit remains visible at tiny token size. Keep the facial features sharply readable, not dissolved into abstract fog. No gore or text.
```

### Shaman

Suggested source file: `assets/source/units/shaman.png`

```text
Create a fantasy Shaman portrait: a cunning, slightly sinister tribal spellcaster with a rugged weathered face, bone-and-wood ornaments, rough layered robes, and a crooked staff with charms beside one shoulder. Earthy olive, umber, ochre and muted turquoise details, confident intense eyes, and a distinct silhouette. Differ visually from the refined crystal-focused Geomancer and the blue-robed Wizard. Avoid face-obscuring masks, excessive skull clutter, gore and text.
```

### Locomotive

Suggested source file: `assets/source/units/locomotive.png`

```text
Create a compact locomotive portrait illustration for a small circular tabletop unit token, matching the hand-painted material texture and overhead-game aesthetic of the existing fantasy board game portraits. A charming but heavy industrial steam locomotive front shown from a slightly elevated frontal three-quarter angle, with a sturdy smokestack, metal boiler nose, headlamp and dark iron wheels partly visible. Warm weathered steel, deep charcoal, brass accents, a touch of steam behind, and a simple muted backdrop. Center the locomotive body and keep its silhouette inside the circular crop-safe region. This is a vehicle portrait rather than an overhead terrain tile. No railway logos, text, labels, UI or token frame.
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

## Additional terrain prompts: Grass, Water and Lava

Generate 5–6 candidates using **one base prompt per terrain type**, then select three visually consistent variants. These are ordinary terrain cells; scenario/objective overlays are separate presentation features.

Selected original files:

```text
assets/source/terrain/
  grass-01.png
  grass-02.png
  grass-03.png
  water-01.png
  water-02.png
  water-03.png
  lava-01.png
  lava-02.png
  lava-03.png
```

Use the existing artwork preparation pipeline to produce 256×256 WebP runtime textures. Preserve original files, and keep deterministic terrain variant selection.

### Grass — base prompt

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down grass tile for a polished tactical fantasy board game.

Create a square top-down grass terrain tile representing one normal grass cell in a board game. The entire tile should read as one continuous patch of short natural grass, not as many small internal tiles or repeated decorative clusters.

Use a calm medium green base with subtle natural variation, soft uneven color shifts, a little dryness and light wear, and restrained texture detail. The grass should look believable and pleasant, but remain visually quiet so unit tokens, grid lines, and gameplay highlights stay easy to read on top of it.

Strictly overhead view. No internal grid, no paving-like divisions, no strong repeated patterns, no flowers, no rocks, no paths, no borders, no text, no symbols, no interface elements, and no watermarks.

The tile should be visually quiet and suitable for repeated use across many adjacent board cells.
```

### Water — base prompt

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down water tile for a polished tactical fantasy board game.

Create a square top-down water terrain tile representing one normal water cell in a board game. The entire tile should read as one continuous patch of water, not as many small internal tiles or decorative clusters.

Use a calm blue-green water surface with subtle tonal variation, soft ripples, and gentle natural movement. Keep the texture readable and visually quiet so unit tokens, grid lines, and gameplay highlights remain easy to read on top of it.

The water should feel like a premium fantasy board game terrain tile: slightly stylized, cohesive, and attractive, but not overly dramatic or visually busy.

Strictly overhead view. No internal grid, no paving-like divisions, no shore, no rocks, no plants, no fish, no foam splashes, no boats, no objects, no borders, no text, no symbols, no interface elements, and no watermarks.

The tile should be visually quiet and suitable for repeated use across many adjacent board cells.
```

### Lava — base prompt

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down lava tile for a polished tactical fantasy board game.

Create a square top-down lava terrain tile representing one normal lava cell in a board game. The entire tile should read as one continuous patch of molten lava, not as many small internal tiles or decorative clusters.

Use glowing orange-red molten lava with darker cooled areas, subtle natural flow patterns, and restrained surface variation. The tile should feel hot and dangerous, but remain readable and visually controlled so unit tokens, grid lines, and gameplay highlights stay easy to read on top of it.

The lava should feel like a premium fantasy board game terrain tile: slightly stylized, cohesive, and dramatic without becoming chaotic or overly bright.

Strictly overhead view. No internal grid, no separate stone border, no rocks protruding from the surface, no objects, no symbols, no interface elements, no text, and no watermarks.

The tile should be visually quiet and suitable for repeated use across many adjacent board cells.
```

### Selecting the three variants

Choose three that read as variations of the same terrain, with similar lighting and scale. Avoid strong central highlights, noisy patterns, unintended internal grids and decorative features that distract from unit tokens or gameplay highlights. Inspect them together on a real board.

## Integrated transparent terrain objects

The supplied `assets/source/terrain/objects/tree-01.png` through `tree-03.png` and `table-01.png` through `table-03.png` are centered 1254 × 1254 RGBA originals with proper transparency. Preserve the entire square composition and original alpha; no background removal, crop or retouching is needed. The existing preparation pipeline exports optimized 256 × 256 sRGB WebP files under `art/terrain/objects/tree/` and `art/terrain/objects/table/`, in the same numeric slot order. Enhanced 2D composes them over the existing Grass and Stone Floor catalogs, respectively, with independent variant and quarter-turn choices. Review the generated composite and catalog previews and then the actual board at Cell size. This integration adds no new generation prompts or gameplay content.

## Railway material kit — generation prompts

The track geometry is **not** AI-generated. It will be constructed with exact, shared dimensions and connection points as described in [VISUAL_STYLE.md](VISUAL_STYLE.md). Generate only the materials below. Match the existing hand-painted Premium Tabletop style and strictly overhead perspective.

### Gravel — base prompt

Generate 5–6 candidates from this **same prompt**, then choose three with similar scale, brightness and color. Save as `assets/source/terrain/gravel-01.png`, `gravel-02.png`, `gravel-03.png`. Gravel is an ordinary standalone terrain as well as Rail's base layer.

```text
Premium fantasy tabletop board game terrain artwork. Hand-painted top-down gravel texture for a polished tactical fantasy board game.

Create one square terrain tile consisting entirely of compacted railway gravel and small crushed stones, seen strictly from directly overhead. Neutral warm gray and dusty brown aggregate, fine irregular pebbles, subtle natural color variation and restrained surface wear. The texture should feel tactile and slightly stylized, consistent with premium illustrated fantasy dungeon floor artwork.

This is a full-bleed ground texture, not a railway track scene. Keep it evenly detailed and visually quiet, without a central focal point or dramatic directional lighting, so character tokens, rails, arrows and grid highlights remain clearly readable on top.

No rails, wooden sleepers, train, path, grass, soil patches, borders, internal grid, square paving stones, perspective, text, symbols, UI or watermarks. Suitable for repeating across adjacent board cells.
```

### Wooden sleeper — single component

Generate a few candidates and select one. Save the chosen **original** as `assets/source/railway/sleeper.png`. It will be resized and placed repeatedly at precise intervals by the track generator. Do not bake ballast or rails into this image.

```text
Premium fantasy tabletop board game component artwork, hand-painted and consistent with a polished fantasy railway tabletop.

A SINGLE old wooden railway sleeper (railroad tie), viewed strictly from directly overhead, lying horizontally across the image. One solid rectangular timber beam with subtle irregular worn edges, visible oak grain, weathering and restrained dark iron fixing marks. Medium-dark warm brown wood with readable natural highlights and shadows, matching the painterly detail level of the game's dungeon and industrial assets.

Center the full sleeper in a square 1:1 image with generous empty margins, keeping both ends visible. It must be a single isolated piece with a genuinely transparent alpha background, not a row of sleepers, and no painted ground shadow or gravel. The sleeper must remain recognizable when reduced to a small strip.

No steel rails, tracks, ballast, surrounding scenery, text, letters, UI, borders, logos or watermarks. Strict overhead, no perspective.
```

### Steel rail — material/style reference

Generate a few candidates and select one. Save the chosen **original** as `assets/source/railway/rail.png`. This image is a **material/reference component**: Codex should construct geometrically precise curved and straight rail paths rather than stretching the image into bends.

```text
Premium fantasy tabletop board game component artwork, hand-painted and consistent with a polished fantasy railway tabletop.

A SINGLE long straight steel railway rail viewed strictly from directly overhead, lying vertically through the center of a square 1:1 composition. Weathered dark iron-gray metal with a slim brighter polished steel running surface and subtle edge shading. Consistent thickness from one end to the other, simple uninterrupted straight silhouette, restrained realistic material detail that remains legible at small size.

The rail is one isolated metal component on a genuinely transparent alpha background with generous clear margins. Show the full length without perspective or vanishing point. It is NOT a pair of rails and NOT a completed railway track.

No sleepers, gravel, wheels, locomotive, bolts scattered around it, scenery, shadows on the ground, text, letters, UI, borders, logos or watermarks.
```

### Integration and selection notes

- Gravel originals are opaque square textures; the three selected variants should look like the same material, not different biomes.
- Sleeper and rail originals must retain real alpha transparency. Verify alpha in the files, not just from a checkerboard-looking image.
- Do not assume generated proportions establish the track gauge or sleeper spacing. The track generator owns those parameters and must produce exact edge-to-edge connections.
- The arrow indicating the Rail Cell's outgoing direction is drawn separately in the client. Do not include arrows in these images.
- The first implementation should verify connected straight/curve/T/cross tracks on a sample board before considering the kit complete.

## Resource and Machine artwork — Coal, Diamond, Diamond Press

Generate several candidates for each subject and choose one cohesive image. Follow the approved **Premium Tabletop** hand-painted style. All assets are strictly overhead, square originals with **genuine alpha transparency** outside the object. The client owns labels, storage counts, machine ownership, highlights and interaction indicators: do not paint these into the image.

### Coal — resource marker

Source: `assets/source/resources/coal.png`

```text
Premium fantasy tabletop board game resource artwork. Hand-painted digital illustration matching a polished tactical fantasy board game.

Create one compact, recognizable small pile of irregular coal chunks viewed strictly from directly overhead. Several rough black and charcoal-gray pieces with angular fractured surfaces, restrained cool highlights, a hint of dusty texture and clear separation between the chunks. The silhouette should remain immediately identifiable at very small marker size on stone, grass or gravel terrain.

Center the pile within a square 1:1 composition with generous transparent margins. Real transparent alpha background, no painted ground, floor, container, sack, mine cart, shadow cast onto a floor, or decorative tile. No text, labels, numbers, border, UI, logos or watermark.
```

### Diamond — resource marker

Source: `assets/source/resources/diamond.png`

```text
Premium fantasy tabletop board game resource artwork. Hand-painted digital illustration matching a polished tactical fantasy board game.

Create one distinctive faceted diamond gemstone viewed from directly overhead or very slightly tilted so its facets remain recognizable. Clear icy pale-blue and white crystalline material with restrained bright glints and cool gray-blue facet shadows. Strong clean silhouette, unmistakably a valuable gem when displayed as a small resource marker. Avoid excessive glow, bloom or tiny scattered sparkles.

Center the single gemstone in a square 1:1 composition with generous transparent margins. Real transparent alpha background. No floor, pedestal, container, pile of gems, frame, text, labels, numbers, UI, logos or watermark.
```

### Diamond Press — Machine occupying one Cell

Source: `assets/source/machines/diamond-press.png`

```text
Premium fantasy-industrial tabletop board game machine artwork. Hand-painted digital illustration matching the existing fantasy board game unit portraits and terrain materials.

Create a single compact, heavy mechanical diamond press viewed strictly from directly overhead. A sturdy industrial apparatus with a square iron-and-brass chassis, thick metal press jaws or rollers, reinforced plates, visible bolts, restrained dark steel wear, and a small hint of coal-fed machinery. It should look like a practical fantasy workshop machine that compresses coal into diamonds, not a modern factory building.

The entire machine fits within a square 1:1 composition with safe margins, a strong readable silhouette and enough detail to remain identifiable at one board Cell in size. Real transparent alpha background outside the machine, without floor, room, tracks or scenery.

No produced diamond lying on the ground, no loose coal piles, no built-in resource counter, no owner color, text, letters, symbols, UI, borders, logos or watermark.
```

### Integration notes

- Verify actual alpha transparency, framing and visibility at normal game Cell/marker size before conversion.
- Resources are small, separately rendered markers and should not include terrain or UI decoration.
- The Machine is a full-cell object; storage and Owner Side remain separate dynamic presentation elements.
- Preserve original images under `assets/source/`. Let the artwork skill determine runtime dimensions and register canonical content IDs from the current repository; do not infer game content from filenames.

## Racing and workshop artwork — Asphalt, Chief Mechanic, Banana Peel

These are **generation prompts**, not game-rule definitions. Match the existing Project Delve hand-painted Premium Tabletop art direction. Generate several candidates and select the most suitable images at actual board scale.

### Asphalt — base prompt (three variants)

Use **one prompt** for 5–6 candidates; select three with similar brightness, texture scale and material character. Save selected square opaque originals as `assets/source/terrain/asphalt-01.png`, `asphalt-02.png`, `asphalt-03.png`. Asphalt is ordinary Terrain; do not paint a racetrack's lines, kerbs or arrows into the texture.

```text
Premium tabletop board game terrain artwork. Hand-painted top-down asphalt surface tile for a polished tactical board game with a cohesive fantasy-industrial aesthetic.

Create a square, seamless-looking full-bleed patch of weathered dark gray asphalt viewed strictly from directly overhead. Fine compacted aggregate, restrained grain, tiny natural tonal irregularities and subtle wear. Medium-dark charcoal gray, but light enough for dark units, resource markers, selection highlights and track markings to remain readable. Even illumination with no strong central highlight or directional shadow.

This is one ordinary board Cell's neutral ground material, not a road scene. Visually quiet, consistent scale, and suitable for placing many adjacent cells together with deterministic rotation. No road markings, painted lines, kerbs, tire tracks, cracks forming strong lines, puddles, debris, borders, internal grid, perspective, vehicles, text, symbols, UI or watermarks.
```

### Chief Mechanic — portrait

Save chosen square original as `assets/source/units/chief-mechanic.png`. Use the **Shared portrait style** section above together with this subject prompt, and preferably attach an approved Worker portrait as a style reference.

```text
Create a fantasy-industrial Chief Mechanic portrait for a premium hand-painted tabletop board game. An experienced workshop foreman and master technician, confident and slightly stern, with a weathered expressive face, practical heavy leather work apron over sturdy dark clothes, brass-rimmed goggles resting on the forehead, sturdy gloves and a few carefully arranged tools visible at the shoulders. Subtle iron, copper and worn brass details suggest responsibility for a busy workshop and mechanical production line.

Distinct from an ordinary Worker: more authoritative, seasoned and capable, but still a hands-on craftsperson rather than a noble or soldier. Warm browns, muted brass and dark slate accents, soft upper-left lighting, subdued background. Square 1:1 head-and-upper-torso composition with all important details in the central 75% for circular cropping; face legible at tiny token size.

No text, numbers, logos, token frame, Side-colored ring, HP indicator, UI, watermarks or face-obscuring equipment.
```

### Banana Peel — Cell Marker

This prompt records the original Banana Peel generation specification for reproducibility. If an approved source image already exists at `assets/source/markers/banana-peel.png`, **keep it**; do not regenerate merely because this prompt was added.

```text
Premium fantasy tabletop board game object artwork. Hand-painted digital illustration matching the established Project Delve visual style.

Create a single discarded yellow banana peel, lying open on the ground, viewed strictly from directly overhead. Three or four curved peel sections spread outward from a small central point, forming a recognizable and slightly comical slipping hazard.

Bright warm yellow with subtle brown spots, natural folds, soft painted highlights and restrained shadows. Strong recognizable silhouette, clearly identifiable as a banana peel even when displayed at approximately 32×32 pixels.

Center the entire banana peel within a square 1:1 composition, occupying approximately 75–85% of the image. Leave transparent margins around it.

Genuinely transparent alpha background. No ground, grass, stone floor, asphalt, plate, whole banana, characters, text, labels, borders, UI elements, logos or watermarks.

Strict overhead view, not isometric or side perspective.
```

## Future prompt candidates

Further Unit/Terrain art. New assets should match the same art direction. Originals remain separate from optimized `wwwroot/art/` files.
