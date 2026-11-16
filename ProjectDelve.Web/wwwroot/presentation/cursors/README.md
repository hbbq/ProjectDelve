# Cursor artwork

`svg/` contains the unmodified SVG masters copied byte-for-byte from
`assets/import/delve_cursor_svg_masters.zip`. Keep authoring changes in the SVG
sources; do not hand-edit generated PNGs or `catalog.json`.

From the repository root, install the locked development dependencies with
`npm ci`, then run:

```sh
npm run generate:cursors
npm run test:cursors
```

The generator uses pinned `@resvg/resvg-js` and `saxes` versions from the lockfile.
It discovers every file in the flat `svg/` directory. Each filename must be a
unique lowercase kebab-case cursor ID starting with a letter followed by `.svg`.
IDs follow `^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$`. Nested directories,
case-insensitive duplicate IDs, missing/empty input, and symbolic links are errors.
Adding a valid SVG and rerunning the command adds it to the catalog automatically.
Windows device names such as `con`, `nul`, `com1`, and `lpt1` are not valid IDs.

To replace a cursor, edit its existing `svg/{id}.svg` and regenerate. To add one,
put a new `{id}.svg` in `svg/`, regenerate, and set `"cursor": "{id}"` on the
appropriate entries in `../profiles.json`. Several profiles can share that ID.
`choose` and `invalid` are used directly by the interaction layer. Missing assets
fall back to the ordinary CSS cursor. Generation reads the checked-in SVG folder;
it does not depend on the original import ZIP. Normal Client builds serve the
generated files and do not run the generator.

Only inert vector SVGs are accepted: `svg`, `g`, `path`, `rect`, `circle`,
`ellipse`, `line`, `polyline`, and `polygon`, with explicitly allowed geometry,
paint, transform, and basic accessibility attributes. A positive finite `viewBox`
is required. Each source file is limited to 1 MiB before parsing or rendering.
Optional viewport dimensions must be positive numeric or `px` values
up to 16384. Scripts, event handlers, styles, references, fonts, images, text,
DTDs/entities, processing instructions, comments, CDATA, and external resources
are rejected. Rendering never loads system fonts or fetches resources.

Each original viewport is centered inside a transparent square without cropping
or stretching. Authored margins remain intact. Outputs are 32, 40, and 48 pixel
8-bit RGBA PNGs in `generated/{size}/{id}.png`. Each output must have visible
artwork and transparent pixels. The generator renders twice and compares bytes;
no timestamp or machine path enters the assets. It validates and renders all
sources before replacing outputs or deleting obsolete generated PNGs. Cleanup is
limited to valid cursor PNG filenames directly inside the three size directories;
unrelated files remain untouched. Existing symlinks in output paths are rejected.

`catalog.json` is generated in sorted ID order:

```json
{
  "schemaVersion": 1,
  "cursors": [
    {
      "id": "attack",
      "images": {
        "32": "./presentation/cursors/generated/32/attack.png",
        "40": "./presentation/cursors/generated/40/attack.png",
        "48": "./presentation/cursors/generated/48/attack.png"
      }
    }
  ]
}
```

The browser consumes this manifest as the image map. The separate authored
`hotspots.json` supplies optional normalized hotspot overrides, independent of
generation:

```json
{ "schemaVersion": 1, "hotspots": { "attack": [0.5, 0.5] } }
```

Each coordinate must be at least 0 and less than 1. Cursors without an override
use the center. Generation does not rewrite this file.
For example, the center is `(16,16)`, `(20,20)`, or `(24,24)` at the three sizes.
The Client's Settings > Display > Cursor size selects Small, Medium (default), or
Large. It persists locally as `delve.cursor.size.v1` and updates mounted cursor
feedback without a reload. This preference never enters game or scenario data.

For isolated tooling tests, the command also accepts `--source directory`,
`--output directory`, and `--manifest file`. These change filesystem destinations;
manifest URLs retain the published browser asset layout above.
