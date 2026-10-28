# Manual miniature import inbox

Export a self-contained GLB from Tripo and name it using the exact canonical Unit
slug: for example `goblin.glb` maps to `goblin-type`. Place it here. Only exact
lowercase catalog slugs are accepted; unrelated files are ignored or reported.
The catalog is checked against the complete Engine registry by existing tests.

From the repository root, using the installed Node 20+ runtime:

```text
node tools/import-unit-models.cjs
```

This read-only batch inspection reuses `inspect-glb.cjs`, the production GLB
preflight, and `prepare-unit-model.cjs`. Its JSON includes source/prepared hashes,
geometry, textures, transformed bounds, target-height normalization, radial/contact
envelopes and base margin. A contact-margin warning is advisory. A hard radius,
geometry, texture, transform or static-format incompatibility defers that file.
Inspect semantic orientation/style and run the Khronos glTF Validator on both
original and prepared bytes; review all warnings and fully decode the images.
Do not resize geometry or change visual dimensions just to pass. Reviewed texture
downscaling is available for memory control as described below.

Before applying, create `assets/source/models/units/<slug>/provenance.json` using
an existing approved record as the format example. Set `status` to
`reviewed-import`. Record the actual source/prepared SHA-256 from inspection,
the standard source/runtime paths, paid-plan evidence, known generator/version,
references, preparation/orientation review, and Khronos validator/version plus
`validation.source.errors` and `validation.runtime.errors` (both must be zero).
Use `unknown` for missing generation facts. Review rights for the actual export;
reuse the established Tripo Pro basis where applicable, without inventing a
public asset license. `commerciallyCleared` and `redistributionRightsConfirmed`
must explicitly be true. Copying a previous record is not evidence for a new
file's plan, hashes, validation, orientation or generation model version.

```text
node tools/import-unit-models.cjs --apply
```

For a reviewed runtime texture limit, use the same option for inspection and apply:

```text
node tools/import-unit-models.cjs --texture-limit 1024
node tools/import-unit-models.cjs --texture-limit 1024 --apply
```

This optional preparation step uses `tools/resize-model-textures.py` with the
existing offline Python/Pillow toolchain. Set `PROJECTDELVE_PYTHON` to the installed
Python executable if `python` is not on PATH; do not pass shell arguments in it.
Record `preparation.textureLimit: 1024` and the Python, Pillow and zlib versions in
provenance. Limits are 256, 512, 1024 or 2048, preserve aspect ratio and never
upscale. The batch uses one shared limit, with no per-Type runtime exceptions.
The helper fully decodes images, applies LANCZOS, writes lossless PNG at compression
level 9 and repacks aligned buffer views. Geometry, materials and bindings remain
unchanged. Originals and original embedded images remain byte-for-byte intact.
Omitting the option keeps the previous exporter byte-for-byte behavior.
The reviewed limit and both hashes must match before apply. Registration records
the resulting texture budget. `prepare-unit-model.cjs <provenance> --check` reads
the recorded option, so it reproduces the prepared export with the same encoder
versions (this batch: Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng).
Pillow is an offline preparation/test dependency only; normal build, publish and
browser loading use checked-in GLBs and require neither Python nor a new package.
The full client suite's optimized-export reproduction tests need this toolchain.

All 26 production miniatures now use runtime maps limited to 1024 pixels after
Tabletop review. The remaining 19-model batch was prepared with that option at
import; the seven earlier runtimes were subsequently reprepared from unchanged
archived originals using their updated `preparation.textureLimit: 1024` records.
For an existing approved model, use its provenance-driven preparation command
below; the inbox importer deliberately refuses to overwrite an existing asset.
Update the reviewed runtime hash/size, preparation and validation records, and
descriptor texture budget together. Preserve all visual dimensions/references.
All 19 passed; Royal King's .033071 Cell contact margin falls .006929 short of the
preferred .04 margin but fits its base and hard radius. No dimension changes.
See `THREE_VIEWPORT.md` for the full inventory, provenance, measurements and QA.

To constrain a task to an explicit batch while other exports arrive, add
`--only goblin,barbarian,cleric,hobby-knight,worker` to either command. Unselected
files are left completely alone, including inspection. Never treat a file's
arrival in the inbox as approval to publish it.

For each reviewed compatible file, this command:

1. Binds the review to exact source and prepared hashes/paths.
2. Writes the deterministic runtime to
   `ProjectDelve.Web/wwwroot/art/models/units/<slug>/v1/<slug>.glb` exclusively.
3. Preserves original bytes at `assets/source/models/units/<slug>/original.glb`,
   verifying SHA-256 before and after, and records both hashes in provenance.
4. Adds the approved production descriptor and the existing Type's `model`
   reference to `art/models/unit-visuals.json`. Visual dimensions stay unchanged.
5. Removes the successful inbox copy only after assets, provenance and catalog
   have been written. No manual asset moving/registration is needed.

Deferred imports stay here; fix their concrete problem and retry independently.
No approved source, runtime or registration is overwritten. Replacements require
a separately reviewed versioned integration. If an I/O failure occurs during
exclusive writes, the inbox remains; inspect any newly created partial output
before retrying. The script deliberately does not delete recovery evidence.
An empty inbox produces `[]` and changes no assets, hashes, registrations or
timestamps. It never scans/reprocesses archived sources.

Finally run focused model/catalog/viewport and hosting tests, the full existing
client/security and .NET suites, and Release build/publish validation. Visually
verify actual gameplay through `BROWSER_INSPECTION.md`: facing after Move/Attack,
grounding/contact, Side bases, Lying/restoration, picking, both lighting/projection
modes, camera angles, FoW, switching, restart/reload and narrow layout. Use
test-owned scenarios and measure a modest mixed scene. Record approval evidence
and any limitations in provenance and `THREE_VIEWPORT.md`.

```text
node tools/prepare-unit-model.cjs assets/source/models/units/<slug>/provenance.json --check
node tools/test-miniature-publish.cjs --release
node tools/batch-model-qa-plans.cjs
```

See `BROWSER_INSPECTION.md` for installed-Node resolution if the nvm shim fails.
The importer adds no watchers, service, UI, packages or production preview flags.
