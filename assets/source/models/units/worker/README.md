# worker production miniature

Imported from the manual inbox using `tools/import-unit-models.cjs --apply` and
the unchanged `tools/prepare-unit-model.cjs` exporter. `provenance.json` records
all source/runtime hashes, before/after move hashes, full source inspection,
reviewed paid Tripo Pro rights, project references and validation.
The exact generation model, date, settings, selected input and export receipt are
unknown. Project reference paths do not imply they were used as generation inputs.

Source: `assets/source/models/units/worker/original.glb` (4343092 bytes).
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/worker/v1/worker.glb` (4343160 bytes).
Source SHA-256: `e87fd84d08f0f4e00ff08a06b27b38e3f67995e4dec8c8d1506b020abadd46aa`.
Runtime SHA-256: `e848ac933336c68b1bc1aef52cc324a956c3a6bf8418c0e16e6a34f059d113bc`.

One static glTF 2.0 identity root, mesh, primitive and opaque PBR material;
9,756 triangles. Three original embedded 2048x2048 textures (base color and
metallic/roughness JPEG, normal PNG), 64 MiB estimated RGBA plus mipmaps.
No skinning, animation, external resource or actual extension payload.
Original geometry and image bytes retained; unused exporter extension declarations
removed and deterministic source-hash metadata added. No simplification, texture
resizing, axis correction or deformation. Khronos source/runtime: zero errors,
one generated-tangent-space warning each; runtime-generated normal-map tangents
can vary between implementations. Source unused-extension info is removed.

Authored height 0.63; normalized X/Y/Z 0.306143 / 0.630000 / 0.279150 Cells.
Measured radius 0.189016 / catalog maximum 0.34.
Low 7.5%-height contact radius 0.189016, base radius 0.28,
margin 0.090984 (target .04); compatible unchanged 1x1 footprint.
Rear sack is visible and retained; it does not enlarge logical picking or the contact envelope.
+Y up; +Z forward visually verified after positive-Z Move in restricted Edge: inspection-v9ddWI/move-positive-z-front.png. Minimum Y zero; runtime places contact at the procedural base top.

Rights reuse the Zombie/Troll paid-user basis, rechecked once for this batch on
2026-10-28: [Tripo terms](https://www.tripo3d.ai/terms), 2025-07-11, section 5.2.2;
[Pro pricing](https://www.tripo3d.ai/pricing). These support publication, distribution
and licensing, subject to the agreement and third-party rights. No separate
public asset license or exclusive copyright claim is invented. Details and
restrictions are preserved in provenance. Source remains outside published wwwroot.

See [the inbox workflow](../../../../import/models/README.md) and repository
`THREE_VIEWPORT.md` for gameplay screenshots, mixed performance and verification.
Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/worker/provenance.json --check`.
