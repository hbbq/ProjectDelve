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

**Art-direction correction:** Earlier candidates looked too similar to Gravel. Asphalt must be noticeably **darker, smoother and more uniform**. The approved Gravel artwork should not be used as a texture reference for Asphalt.

```text
Premium tabletop board game terrain artwork. Hand-painted top-down asphalt surface tile for a polished tactical board game with a cohesive fantasy-industrial aesthetic.

Create a square, full-bleed surface of smooth, densely compacted dark asphalt, viewed strictly from directly overhead.

The asphalt should be deep charcoal gray, almost black, with a subtle fine-grained matte texture. The surface is relatively smooth and uniform, like a maintained paved racing circuit rather than a gravel road.

Use restrained tonal variation, faint natural wear and very fine surface grain. Individual stones and pebbles must NOT be visible. Avoid coarse aggregate, rough gravel texture, large cracks, patches and strong discoloration.

The result should be visibly darker, smoother and more uniform than the game's existing gray-brown Gravel terrain, while remaining readable beneath unit tokens, resource markers and gameplay highlights.

Soft, even illumination. No strong directional lighting, central highlight or vignette.

Strictly overhead view. No road markings, painted lines, kerbs, tire tracks, puddles, debris, borders, internal grid, perspective, vehicles, text, symbols, UI or watermarks.

The texture should be visually quiet and suitable for repeated use across many adjacent board cells, including deterministic quarter-turn rotation.
```

### Chief Mechanic — portrait

Save chosen square original as `assets/source/units/chief-mechanic.png`. Use the **Shared portrait style** section above together with this subject prompt, and preferably attach an approved Worker portrait as a style reference.

```text
Premium tabletop board game terrain artwork. Hand-painted top-down asphalt surface tile for a polished tactical board game with a cohesive fantasy-industrial aesthetic.

Create a square, full-bleed surface of smooth, densely compacted dark asphalt, viewed strictly from directly overhead.

The asphalt should be deep charcoal gray, almost black, with a subtle fine-grained matte texture. The surface is relatively smooth and uniform, like a maintained paved racing circuit rather than a gravel road.

Use restrained tonal variation, faint natural wear and very fine surface grain. Individual stones and pebbles must NOT be visible. Avoid coarse aggregate, rough gravel texture, large cracks, patches and strong discoloration.

The result should be visibly darker, smoother and more uniform than the game's existing gray-brown Gravel terrain, while remaining readable beneath unit tokens, resource markers and gameplay highlights.

Soft, even illumination. No strong directional lighting, central highlight or vignette.

Strictly overhead view. No road markings, painted lines, kerbs, tire tracks, puddles, debris, borders, internal grid, perspective, vehicles, text, symbols, UI or watermarks.

The texture should be visually quiet and suitable for repeated use across many adjacent board cells, including deterministic quarter-turn rotation.
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

## Diamond Kart — racing unit portrait

Save the selected square original as `assets/source/units/diamond-kart.png`. Use the shared portrait style as a painterly/material reference, but compose this as a **vehicle-and-driver token illustration** rather than a humanoid portrait. Generate several candidates and select one readable in the circular token. Do not generate a full racetrack or bake in a Side-colored ring.

```text
Premium fantasy-industrial tabletop board game artwork. Hand-painted digital illustration matching the established Project Delve unit portraits and machine assets.

Create one charming, compact fantasy go-kart racing vehicle WITH A GOBLIN DRIVER seated behind the steering wheel, viewed from a slightly elevated three-quarter overhead angle. The kart's silhouette, four wheels, seat, steering wheel and small brass-and-steel engine should be instantly recognizable. A playful mechanical kart assembled from painted metal, weathered steel, leather and brass, combining old workshop engineering with energetic racing.

The driver is a mischievous green-skinned fantasy goblin with pointed ears, an expressive determined grin, and simple driving goggles or a leather cap. Make the goblin visibly seated in and actually driving the kart, with hands on the steering wheel. Keep the face, ears and driving pose readable at small token size, without hiding the vehicle's recognizable outline.

Bold readable combined silhouette, restrained warm highlights, dark tires, a modest bright red painted body with neutral metal details. A small diamond-shaped ornament on the kart is acceptable, but no loose resource diamond or banana peel.

Square 1:1 composition with the complete kart and driver centered comfortably inside the central 75% safe area for a circular board-game token crop. Simple subdued atmospheric background matching the existing painted unit portraits. Ensure both kart and goblin remain recognizable when displayed very small.

No track, road markings, text, racing number, brand logo, token frame, Side color ring, HP badge, UI or watermark.
```

## Fruit Tree and racing item artwork

Use the established hand-painted Premium Tabletop style. Generate multiple candidates and choose those readable at normal board size.

### Fruit Tree — three transparent variants

Generate 5–6 candidates from one prompt and choose three. Save as `assets/source/terrain/objects/fruit-tree-01.png` through `fruit-tree-03.png`. These are overlays on existing Grass, like the ordinary Tree overlays.

```text
Premium fantasy tabletop board game object artwork. Hand-painted digital illustration matching the existing Project Delve top-down tree canopy overlays.

Create one mature fruit tree seen strictly from directly overhead, showing a dense irregular leafy green canopy with many clearly visible vivid red cherries among the leaves. The cherries should be slightly exaggerated in size and contrast, so the tree is unmistakably a fruit tree at one-cell size. Rich natural greens, darker foliage clusters, subtle highlights and a strong organic silhouette.

Center the canopy in a square 1:1 composition, occupying approximately 75–85% of the image width. Genuinely transparent alpha outside the canopy. No grass, dirt, ground, tile, other trees, cast shadow on ground, border, text, UI or watermark.

Strictly overhead, no isometric perspective. Suitable for deterministic quarter-turn rotation over a separate grass texture.
```

### Fruit — cherry resource

Source: `assets/source/resources/fruit.png`

```text
Premium fantasy tabletop board game resource artwork. Hand-painted digital illustration matching Project Delve's Coal and Diamond resource markers.

Create a classic retro-arcade-style pair of ripe bright red cherries joined by curved green stems with a small leaf. Bold unmistakable silhouette, restrained glossy highlights, simple readable forms, strictly overhead or slight overhead tilt. This is the generic resource named Fruit, represented visually by cherries.

Centered square 1:1 composition with generous genuine transparent alpha margins. Immediately recognizable at approximately 32x32 pixels. No basket, bowl, tree, floor, text, UI, border or watermark.
```

### Banana — whole fruit resource

Source: `assets/source/resources/banana.png`

```text
Premium fantasy tabletop board game resource artwork. Hand-painted digital illustration matching Project Delve's small resource icons.

Create one intact ripe yellow banana, gently curved, with a clear bold silhouette, subtle warm shading, tiny brown freckles and restrained painted highlights. Strict overhead or very slight overhead tilt. Clearly a whole unpeeled banana, not a discarded banana peel. Readable at 32x32 pixels.

Centered square 1:1 composition with generous genuine transparent alpha margins. No ground, plate, banana bunch, text, UI, border or watermark.
```

### Mushroom — Boost resource

Source: `assets/source/resources/mushroom.png`

```text
Premium fantasy tabletop board game collectible item artwork. Hand-painted digital illustration matching Project Delve's small resource icons.

Create one cheerful fantasy power-up mushroom with a rounded bright red cap, a few large cream-white spots and a short pale stem. Slight overhead angle to keep cap and stem visible. Bold compact silhouette and restrained painterly highlights. Original arcade-fantasy design without copying any existing branded character. Readable at 32x32 pixels.

Centered square 1:1 composition with generous genuine transparent alpha margins. No face, eyes, scenery, text, UI, border or watermark.
```

### Shell — racing projectile resource

Source: `assets/source/resources/shell.png`

```text
Premium fantasy tabletop board game collectible item artwork. Hand-painted digital illustration matching Project Delve's small resource icons.

Create one compact green fantasy racing shell projectile, viewed from a three-quarter overhead angle. Rounded domed emerald-green segmented shell plates, muted pale rim, subtle highlights, clear geometric silhouette and painterly material texture. An original unbranded design. No creature, head, legs or branded-character details. Readable at 32x32 pixels.

Centered square 1:1 composition with generous genuine transparent alpha margins. No floor, track, motion effects, text, UI, border or watermark.
```

### Slot Machine — one-cell Machine

Source: `assets/source/machines/slot-machine.png`

```text
Premium fantasy-industrial tabletop board game machine artwork. Hand-painted digital illustration matching Project Delve's Diamond Press and workshop assets.

Create one whimsical mechanical slot machine viewed strictly from directly overhead, designed as a compact one-cell tabletop board game machine. Sturdy iron-and-brass cabinet, three distinct mechanical reel windows with simple fruit, mushroom and shell motifs, a prominent side lever, small warm indicator lamps, rivets, worn metal edges and playful clockwork engineering. Immediately recognizable as a slot machine even from above. Weathered brass, dark steel and muted cherry-red colors, restrained highlights and readable large forms.

Center the entire machine in a square 1:1 composition with genuine transparent alpha margins. No workshop floor, loose resources, Workers, road, labels, numbers, owner colors, storage counter, UI, frame, logo or watermark. Strict overhead, no isometric perspective.
```

### Integration guidance

Preserve originals. Fruit Tree is Grass plus three transparent, deterministically varied and rotated canopy overlays, using the existing Tree artwork mechanism. Banana is a Resource distinct from the Banana Peel Cell Marker. Resource icons should remain legible at small size; Machine storage and ownership indicators remain client-rendered. Verify real alpha transparency and in-board appearance.

## Delve Chess — first portraits (Peasant and Rook)

These four portraits establish two contrasting courts. **Hero Court** uses real, professionally made medieval equipment and restrained blue/silver/gold heraldry; **Monster Court** is a self-serious Goblin/Troll amateur imitation assembled from wood, scrap iron, rope and oversized costumes. Both courts should have equally polished *artwork*, and the characters are equally capable mechanically. Keep the visual silhouette of a chess pawn or rook, but draw living fantasy characters, **not literal chess pieces**.

For each portrait, **combine the Shared portrait style prompt above with the subject prompt below**. Prefer attaching one approved Project Delve unit portrait as a style reference, and use the first approved Chess portrait as a reference for the other three. Square 1:1, head and upper torso, central 75% safe for circular crop, face readable, no printed letters, border, side-color ring, or UI. These are portraits, **not** top-down miniatures or full-body chess board illustrations.

Save untouched originals as:

- `assets/source/units/royal-peasant.png`
- `assets/source/units/goblin-peasant.png`
- `assets/source/units/castle-guard.png`
- `assets/source/units/tower-troll.png`

The names and filenames are proposed artwork identifiers; the actual Unit Type IDs and gameplay definitions will be confirmed in `CONSTRAINTS.md` before implementation.

### Royal Peasant — Hero Court pawn

```text
Create a portrait of a humble but proud human medieval peasant conscript serving a proper royal chess court. The central visual motif is a large smooth rounded steel helmet with a distinctive spherical crown that immediately evokes the silhouette of a chess pawn, but is still believable wearable equipment. His expressive, slightly apprehensive human face is clearly visible below the helmet. Modest but well-made blue-and-cream padded tunic, simple polished shoulder protection, a hint of a wooden spear shaft by the shoulder. Equipment is real and practical, though clearly much less prestigious than the royal knights. Restrained silver, muted royal blue and warm leather palette. Charming, dignified, lightly humorous without caricature. Keep the rounded pawn-helmet shape conspicuous within a circular token crop.
```

### Goblin Peasant — Monster Court pawn

```text
Create a portrait of a small green fantasy Goblin trying very earnestly to impersonate a royal chess pawn. He wears an absurdly oversized round helmet hammered together from dented scrap metal and a rounded cooking pot, with visible seams, rivets and improvised straps. The helmet's large spherical top must instantly evoke a chess pawn silhouette, while the Goblin's pointed ears, green face and determined overconfident expression remain clearly visible. Patched brown tunic, bits of rusty metal armor, a crooked wooden spear shaft by one shoulder. Handmade costume assembled by enthusiastic amateurs, not a professionally forged uniform. Funny through design and character, not a cartoon or poorly rendered image. Earthy browns, oxidized iron, muted green skin and tiny dark-purple cloth accents. Make the round pawn helmet and Goblin face readable at token size.
```

### Castle Guard — Hero Court rook

```text
Create a portrait of a stout, formidable Dwarf castle guard serving as the royal chess court's Rook. Heavy, professionally crafted silver-steel plate armor with refined blue heraldic accents and subtle gold trim. His imposing helmet and shoulder armor deliberately form the unmistakable squared battlement silhouette of a medieval castle tower, with clear crenellations visible above his face. A thick, well-kept beard and stern Dwarven face show through the front opening. Solid, immovable, disciplined and dignified; the armor looks functional and expensive, not a literal stone building. Keep the distinctive castle-tower crown and face large and readable in a circular crop. A muted stone background only, not an actual chessboard.
```

### Tower Troll — Monster Court rook

```text
Create a portrait of a huge green-gray fantasy Troll proudly disguised as a chess Rook, wearing an outrageously improvised castle-tower costume. A crude square turret made of mismatched wooden planks, rough stone-colored painted boards, rope, crooked iron brackets and visible nails sits around his shoulders and head. Four prominent uneven battlements create an unmistakable rook/chess-tower silhouette. His broad tusked Troll face peers through a roughly cut window in the tower costume, looking intensely serious and pleased with the engineering. The outfit is clearly homemade and slightly ridiculous, but solid enough to wear; do not make the creature a literal building or hide the face. Rough wood, rusty iron, gray stone paint, green-gray skin and small dark-purple fabric details. Strong clean silhouette suitable for a small circular token portrait.
```

### Integration notes

- Hero and Monster counterparts have different artwork and names but are intended to have **identical stats, abilities and behavior** within each role. Art should not suggest different power levels.
- Preserve source originals. Runtime derivatives are produced by the existing artwork pipeline.
- Inspect all four at actual token size; the pawn's round cap and rook's crenellations should be recognizable even if finer costume details disappear.
- This batch covers only Peasant and Rook. Bishop, Knight, Queen and King artwork will be specified later.

## Delve Chess — second portraits (Bishop and Knight)

Continue the two-court art direction established in the Peasant/Rook batch above: **Hero Court** has genuine, finely crafted medieval equipment (silver, muted blue, restrained gold); **Monster Court** is a dead-serious but hilariously improvised imitation by Goblins (patched fabric, scrap iron, wood, rope). Both are painted to the same premium quality. The chess references must be readable through the **silhouette**, while the subjects remain living fantasy characters rather than literal chess pieces.

**For each image, concatenate the Shared portrait style prompt at the top of this file with the subject prompt below.** Use an approved Project Delve portrait and, preferably, the matching court's approved Peasant/Rook portrait as visual references. Square 1:1 head-and-upper-torso portrait for a circular token crop; keep the face and distinguishing hat/horse silhouette inside the central 75%. No text, labels, chessboard, UI, border, token ring or watermark.

Save original generated images unchanged at:

- `assets/source/units/royal-bishop.png`
- `assets/source/units/goblin-bishop.png`
- `assets/source/units/royal-knight.png`
- `assets/source/units/hobby-knight.png`

These are artwork filenames, not a decision about canonical Unit Type IDs. Bishop is planned to have diagonal-corridor movement via orthogonal steps and AdvanceOnKill; Knight is planned to have an L-shaped leap. **Do not depict special attacks, magic or rearing/stomping effects**: the horse's Rear Up ability is deliberately deferred.

### Royal Bishop — Hero Court

```text
Create a premium fantasy portrait of a dignified human royal bishop/cleric dressed for a ceremonial chess court. The character wears a tall, authentic and beautifully made bishop's mitre (mitra), with a prominent split-pointed silhouette that immediately reads as the Bishop chess piece's distinctive tall head. His intelligent, slightly severe face is clearly visible beneath the mitre. Finely tailored cream-white and muted royal-blue vestments, delicate gold embroidery, a restrained polished silver clasp, and a slender ceremonial staff visible beside one shoulder. The mitre is the visual focal point and must remain unmistakable in a small circular token crop. Authentic, functional fantasy-clerical clothing; solemn, composed, authoritative, with a hint of theatrical self-importance. Soft atmospheric stone backdrop. No religious symbols copied from real institutions, no spell effects, no full-body pose, no chessboard or literal stone chess piece.
```

### Goblin Bishop — Monster Court

```text
Create a premium fantasy portrait of a small green Goblin who has enthusiastically appointed himself Bishop of a makeshift chess army. He wears a comically gigantic, crooked, split-pointed bishop's mitre, badly stitched from stiff cloth, cardboard-like painted leather and wooden supports, with obvious mismatched seams, patches and loose stitching. The mitre is unmistakably Bishop-shaped but absurdly oversized; the Goblin's pointed ears poke out on either side and his face is still clearly visible, wearing an extremely solemn, self-important expression. His ceremonial robes are patched dark-purple and dusty brown cloth with uneven homemade gold-colored trim, and he holds a crooked stick as a ceremonial staff. Rich painterly texture and credible materials: the COSTUME is amateurish, not the illustration. Keep the giant mitre, face and upper torso clearly readable in a small circular token crop. No spell effects, no literal chess piece, no text, no board.
```

### Royal Knight — Hero Court

```text
Create a premium fantasy tabletop portrait of a genuine royal cavalry knight mounted on a real, noble horse. The knight wears well-made silver-steel armor with muted royal-blue heraldic fabric and restrained gold detailing; the horse has a finely crafted bridle and tasteful blue-and-silver barding. Frame the knight's armored upper body and the horse's expressive head and arched neck together in a compact, cohesive portrait, with the horse's head forming the instantly recognizable chess Knight horse-head silhouette. The rider's face should be at least partly visible beneath an open-faced helmet. Both rider and horse are dignified and believable, confident and disciplined. Compose specifically for a square 1:1 image and small circular token crop: the horse head and rider must both remain visible inside the central 75%, without requiring a full-body view. Simple subdued atmospheric background. No galloping motion blur, no combat, no rearing pose, no text, no chessboard, no literal chess piece.
```

### Hobby Knight — Monster Court

```text
Create a premium fantasy tabletop portrait of a green Goblin pretending to be a grand cavalry knight while riding a ridiculous homemade HOBBY HORSE: a painted wooden horse head attached to a long stick held between his legs. The Goblin is wearing mismatched dented scrap-metal armor, a crooked bucket-like knight helmet and a small patched dark-purple cape. He looks fiercely proud and convinced he is mounted on a magnificent warhorse. The wooden horse head must be unmistakably a toy: visible wood grain, hand-painted eyes, crude carved ears, an improvised rope bridle and part of the wooden stick. Frame the Goblin's face and upper body together with the oversized toy horse head in a clear chess Knight-like horse-head silhouette. Make the absurdity obvious even at small circular token size, while preserving the same high-quality hand-painted fantasy artwork as the Hero Court. Avoid depicting a real horse, a centaur, a Goblin physically seated on an animal, a rearing action, a full-body scene, a literal chess piece, text or chessboard.
```

### Integration notes

- Use the existing Unit portrait artwork pipeline and preserve the originals.
- Hero/Monster counterparts must be equally legible and stylistically cohesive; their gameplay will be symmetric within each role.
- Prioritize the Bishop mitre silhouette and the Knight horse-head silhouette over small decorative details.
- Review circular crops especially carefully: the Knight requires both horse head and rider, while the Goblin's hobby-horse must not look like a real horse.
- This batch covers only Bishop and Knight. Queen and King remain for a later batch.

## Delve Chess — final portraits (Queen and King)

This batch completes the two contrasting chess courts established by Peasant/Rook and Bishop/Knight. **Hero Court:** genuine, elegant medieval royalty, expertly crafted silver/blue/gold clothing and equipment. **Monster Court:** Goblins and other monsters earnestly imitating royal splendor with oversized crowns, patched fabric, salvaged ornaments and homemade finery. Both sides have **identical mechanics per role** and must receive equally polished illustration quality. These are fantasy characters dressed to evoke chess silhouettes, not living stone or wooden chess pieces.

**For each portrait combine the Shared portrait style prompt near the beginning of this file with the specific subject prompt below.** Use approved Project Delve portraits and especially the approved Chess portraits from the matching court as visual style references. Compose for a square 1:1 **head-and-upper-torso portrait** with face and crown in the central 75% safe area for a circular token crop. No text, labels, chessboard, UI, side-colored token rings, borders, logos or watermark. These are not top-down unit sprites or full-body scenes.

Save the four unchanged originals at:

- `assets/source/units/royal-queen.png`
- `assets/source/units/witch-queen.png`
- `assets/source/units/royal-king.png`
- `assets/source/units/goblin-king.png`

The design decision is that both Queens are Unique, powerful mobile combatants with AdvanceOnKill; both Kings are Unique, comparatively weak melee combatants with high HP and **the same adjacent-friendly +1 DEF aura as Cleric**. Do **not** depict Fireball, spellcasting or a Wizard King: the King is a monarch and protector, not a Wizard. The Queen's costume may suggest witchcraft, but the two Queens have the same non-spellcasting gameplay abilities at this stage.

### Royal Queen — Hero Court

```text
Create a premium fantasy tabletop portrait of a formidable human Royal Queen, unmistakably the most powerful battlefield figure of a dignified medieval chess court. She wears an elegant, distinctively tall and ornate queen's crown with several upward points, finely crafted silver-steel ceremonial battle armor and royal-blue velvet accents, with restrained gold filigree. Intelligent, confident, commanding expression; face clearly visible, no closed helmet. A hint of a richly embroidered royal mantle over the shoulders. She looks capable of fighting at the front rather than merely posing for a royal painting, but do not show an active attack. Her queen-shaped crown and regal bearing should read instantly at small circular token size and visually complement the Hero Court's Castle Guard and Royal Bishop. Subdued stone atmospheric backdrop. No staff, no spell effects, no fire, no chessboard or literal chess piece.
```

### Witch Queen — Monster Court

```text
Create a premium fantasy tabletop portrait of a self-appointed Goblin Witch Queen, an eccentric but genuinely intimidating counterpart to a royal chess Queen. Green skin, pointed ears, sly confident eyes, and an enormous crooked queen's crown assembled from scavenged brass, bent iron prongs, mismatched imitation jewels and hand-tied ornaments. Her extravagant dark-purple gown and royal mantle are patched together from velvet scraps, frayed lace and homemade gold trim; a few little trinkets suggest an amateur witch's wardrobe, but she is NOT casting a spell. The crown should have the recognizable tall multi-point silhouette of a chess Queen and remain readable inside a circular token crop. She takes her royal authority completely seriously; the humor comes from the absurdly ambitious homemade costume, not cartoon drawing quality. Rich painterly textures, expressive face, simple atmospheric backdrop. No magic effects, fireball, wand, literal chess piece, chessboard, text or watermark.
```

### Royal King — Hero Court

```text
Create a premium fantasy tabletop portrait of a venerable human Royal King, a real medieval monarch in the dignified Hero Court. An imposing but tasteful royal crown with a clear central high finial evoking the traditional chess King silhouette, finely crafted gold and silver details, royal-blue mantle, polished but restrained ceremonial armor, and a well-groomed beard. He is mature, resolute, protective and authoritative, more a leader who inspires the soldiers standing beside him than a frontline warrior. His expression conveys steady courage rather than magical power. Make his crown visually distinct from the Royal Queen's multi-point crown, and keep his face and crown large and readable at small circular token size. The aura of protection is conveyed through posture and presence, not glowing spell effects. Soft subdued stone background. No wizard hat, staff, Fireball, spell effects, chessboard, literal chess piece or text.
```

### Goblin King — Monster Court

```text
Create a premium fantasy tabletop portrait of a tiny, extremely self-important green Goblin King who has dressed himself as the supreme monarch of a homemade chess army. His oversized, slightly lopsided royal crown is cobbled together from hammered brass, scrap iron, fake gemstones and a conspicuous tall central finial, giving the unmistakable silhouette of a chess King. His pointed ears protrude awkwardly under the crown. He wears an absurdly voluminous patched dark-purple-and-crimson royal cape, a crooked improvised clasp and a homemade ceremonial breastplate; perhaps he grips a short painted wooden scepter, clearly a symbol of authority rather than a magic wand. He looks fiercely serious, commanding and convinced that his nearby subjects will protect him. The visual joke is his improvised royal grandeur, not incompetence or low-quality artwork. Keep the expressive Goblin face and tall crown inside the central circular crop-safe area. No spellcasting, Fireball, glowing aura, wizard clothing, chessboard, literal chess piece, text or watermark.
```

### Integration notes

- All four are Unique character portraits, not generic crowd sprites. Different IDs/names/art but mechanically mirrored Hero/Monster counterparts.
- Preserve originals and prepare runtime artwork with the existing pipeline.
- Queen and King should be immediately distinguishable by crown shape and bearing, including in small circular tokens.
- Hero Court should look genuinely royal; Monster Court should look proudly homemade, **not** visually inferior in artistic execution.
- Avoid magical attack imagery on either King; his ability is the established Cleric-like DEF aura.
- The complete 8×8 chess-inspired scenario can be authored later; these prompts only define artwork.

## Solid Rock — dungeon terrain (three variants)

Solid Rock is a full-Cell, LOS-blocking and impassable terrain type with the same gameplay behavior as Tree. These assets are **presentation only**; do not change terrain rules, Phase, FoW or movement.

Generate **5–6 candidates using the same prompt** and select **three cohesive variants**. Save the approved, untouched square originals as:

- `assets/source/terrain/solid-rock-01.png`
- `assets/source/terrain/solid-rock-02.png`
- `assets/source/terrain/solid-rock-03.png`

Use an approved Stone Floor terrain image as a style reference if helpful, but make Solid Rock visibly different from traversable paving. Unlike Tree, this is a **full-bleed terrain base**, not a transparent object overlay. It must work in large contiguous areas of generated dungeons.

### Image-generation prompt

```text
Premium fantasy tabletop board game terrain artwork, matching the polished hand-painted top-down Project Delve dungeon tiles. Square 1:1 seamless tileable texture of solid, unexcavated bedrock seen STRICTLY from directly overhead.

A dense continuous mass of rough natural stone, with broad irregular interlocking rock planes, shallow fractured seams, worn mineral grain and subtle changes in surface height suggested through painterly shading. Cool neutral slate gray and charcoal-gray stone with restrained muted earthy undertones; moderate contrast, enough texture to unmistakably read as solid impassable rock rather than a walkable stone floor.

Make the stone feel solid, ancient and compact, with a clear chunky material character that remains recognizable when each tile is reduced to a small board Cell. No dominant central boulder or isolated object. Keep the texture distributed evenly across the square, with natural variation and no obvious repeating motif. The texture must tile seamlessly on all four edges, so many copies form one continuous rock mass without visible square boundaries or repeating seams.

Maintain the existing premium fantasy tabletop illustration style, controlled colors, subtle brushwork and gentle consistent overhead illumination. Avoid deep black fissures, strong directional cast shadows, excessive fine noise or bright highlights that compete with Units, grid lines, Fog of War and gameplay overlays.

No perspective, isometric angle, cliffs, visible vertical rock faces, cave openings, holes, tunnels, paths, walkable flagstones, constructed masonry, separate stones arranged like paving, vegetation, moss clusters, water, lava, objects, characters, grid, borders, labels, symbols, text, interface elements or watermark.

Full-bleed opaque square terrain texture, not a transparent overlay. No baked-in Cell outline or border.
```

### Selection and integration guidance

- Select three variants with similar overall brightness, color, texture scale and edge continuity. Reject candidates that look like ordinary Stone Floor, a single isolated boulder, a cave entrance, or a strongly directional cliff face.
- Verify seamless repetition in a 3×3 or larger tiled preview. A seamless-looking single square may still reveal obvious grid seams when repeated.
- Prefer restrained broad shapes over noisy microdetail: this terrain will often occupy a large percentage of a generated board.
- Preserve the original PNG files unchanged. Let the artwork workflow prepare optimized runtime variants, stable deterministic selection and optional quarter-turn rotation if supported by the current catalog.
- Verify the result inside both the normal and zoomed Enhanced 2D board, especially next to Stone Floor, Walls, Doors and FoW. Preserve a distinct Classic fallback. Do not bake any gameplay overlays into the artwork.
- This batch is only the artwork for existing Solid Rock. Do not introduce a new terrain type or change gameplay.

## Future prompt candidates

Further Unit/Terrain art. New assets should match the same art direction. Originals remain separate from optimized `wwwroot/art/` files.
