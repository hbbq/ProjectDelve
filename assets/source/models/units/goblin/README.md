# goblin production miniature

Imported from the manual inbox using `tools/import-unit-models.cjs --apply` and
the unchanged `tools/prepare-unit-model.cjs` exporter. `provenance.json` records
all source/runtime hashes, before/after move hashes, full source inspection,
reviewed paid Tripo Pro rights, project references and validation.
The exact generation model, date, settings, selected input and export receipt are
unknown. Project reference paths do not imply they were used as generation inputs.

Source: `assets/source/models/units/goblin/original.glb` (5008652 bytes).
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/goblin/v1/goblin.glb` (4032532 bytes).
Source SHA-256: `45836ed0b6d593be49e3a5da31ef4646d9a18ad2bb4054ae5584f408d2963e30`.
Runtime SHA-256: `448c1b490d8e8d477451648fc42d36be3c1fc919286b3ebd5c8c19ed9d8db8d0`.

One static glTF 2.0 identity root, mesh, primitive and opaque PBR material;
9,852 triangles. The original retains three embedded 2048x2048 textures (base color and
metallic/roughness JPEG, normal PNG). Runtime uses three 1024x1024 lossless PNGs,
16 MiB estimated RGBA plus mipmaps (previously 64 MiB).
No skinning, animation, external resource or actual extension payload.
Original GLB bytes and runtime geometry/UVs/material bindings are retained;
unused exporter extension declarations are removed and source-hash metadata added.
Runtime maps are resized with the established 1024 limit; no simplification,
axis correction or deformation. Khronos source/runtime: zero errors,
one generated-tangent-space warning each; runtime-generated normal-map tangents
can vary between implementations. Source unused-extension info is removed.

Authored height 0.55; normalized X/Y/Z 0.248413 / 0.550000 / 0.169820 Cells.
Measured radius 0.131841 / catalog maximum 0.32.
Low 7.5%-height contact radius 0.131841, base radius 0.27,
margin 0.138159 (target .04); compatible unchanged 1x1 footprint.
Small body remains readable at authored .55 height; ample base margin.
+Y up; +Z forward visually verified after positive-Z Move in restricted Edge: inspection-FxuJx8/move-positive-z-front.png. Minimum Y zero; runtime places contact at the procedural base top.

Rights reuse the Zombie/Troll paid-user basis, rechecked once for this batch on
2026-10-28: [Tripo terms](https://www.tripo3d.ai/terms), 2025-07-11, section 5.2.2;
[Pro pricing](https://www.tripo3d.ai/pricing). These support publication, distribution
and licensing, subject to the agreement and third-party rights. No separate
public asset license or exclusive copyright claim is invented. Details and
restrictions are preserved in provenance. Source remains outside published wwwroot.

See [the inbox workflow](../../../../import/models/README.md) and repository
`THREE_VIEWPORT.md` for gameplay screenshots, mixed performance and verification.
Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/goblin/provenance.json --check`.

## Runtime texture standardization — 2026-10-28

Runtime: **4,032,532 bytes**, SHA-256
`448c1b490d8e8d477451648fc42d36be3c1fc919286b3ebd5c8c19ed9d8db8d0`.
The source hash is unchanged. `preparation.textureLimit: 1024` drives the same
`prepare-unit-model.cjs` command above; `--check` reproduces these bytes using
Python 3.14.7, Pillow 13.0.0 and zlib 1.3.1.zlib-ng. All three embedded runtime
channels are 1024x1024; geometry, materials, orientation and visual dimensions
remain unchanged. These images are RGB and the material remains OPAQUE.
Khronos validation: zero errors, unchanged generated-tangent-space warning.
Estimated RGBA plus mipmaps: 64 to 16 MiB; this is not measured GPU allocation.
See `THREE_VIEWPORT.md`, "Runtime texture standardization to 1024", for batch
sizes, visual evidence, reproducibility and Release publish verification.
