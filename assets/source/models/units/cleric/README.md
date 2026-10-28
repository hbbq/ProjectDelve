# cleric production miniature

Imported from the manual inbox using `tools/import-unit-models.cjs --apply` and
the unchanged `tools/prepare-unit-model.cjs` exporter. `provenance.json` records
all source/runtime hashes, before/after move hashes, full source inspection,
reviewed paid Tripo Pro rights, project references and validation.
The exact generation model, date, settings, selected input and export receipt are
unknown. Project reference paths do not imply they were used as generation inputs.

Source: `assets/source/models/units/cleric/original.glb` (4159960 bytes).
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/cleric/v1/cleric.glb` (4160028 bytes).
Source SHA-256: `9276845b771a00099032dcaa83c30d16ff5a8ac4194711d44e299bb9684ca967`.
Runtime SHA-256: `8d7fb6a08847d3b52bb6ca3ed769926dd5a0d6ec2f735e5e2d5233c87c0050b5`.

One static glTF 2.0 identity root, mesh, primitive and opaque PBR material;
9,854 triangles. Three original embedded 2048x2048 textures (base color and
metallic/roughness JPEG, normal PNG), 64 MiB estimated RGBA plus mipmaps.
No skinning, animation, external resource or actual extension payload.
Original geometry and image bytes retained; unused exporter extension declarations
removed and deterministic source-hash metadata added. No simplification, texture
resizing, axis correction or deformation. Khronos source/runtime: zero errors,
one generated-tangent-space warning each; runtime-generated normal-map tangents
can vary between implementations. Source unused-extension info is removed.

Authored height 0.78; normalized X/Y/Z 0.346296 / 0.780000 / 0.286736 Cells.
Measured radius 0.194711 / catalog maximum 0.39.
Low 7.5%-height contact radius 0.187447, base radius 0.32,
margin 0.132553 (target .04); compatible unchanged 1x1 footprint.
Staff tip and robes retained; ground-contact band includes staff/hem and fits the base.
+Y up; +Z forward visually verified after positive-Z Move in restricted Edge: inspection-ejjQFR/move-positive-z-front.png. Minimum Y zero; runtime places contact at the procedural base top.

Rights reuse the Zombie/Troll paid-user basis, rechecked once for this batch on
2026-10-28: [Tripo terms](https://www.tripo3d.ai/terms), 2025-07-11, section 5.2.2;
[Pro pricing](https://www.tripo3d.ai/pricing). These support publication, distribution
and licensing, subject to the agreement and third-party rights. No separate
public asset license or exclusive copyright claim is invented. Details and
restrictions are preserved in provenance. Source remains outside published wwwroot.

See [the inbox workflow](../../../../import/models/README.md) and repository
`THREE_VIEWPORT.md` for gameplay screenshots, mixed performance and verification.
Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/cleric/provenance.json --check`.
