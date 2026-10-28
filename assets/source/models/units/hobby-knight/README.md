# hobby-knight production miniature

Imported from the manual inbox using `tools/import-unit-models.cjs --apply` and
the unchanged `tools/prepare-unit-model.cjs` exporter. `provenance.json` records
all source/runtime hashes, before/after move hashes, full source inspection,
reviewed paid Tripo Pro rights, project references and validation.
The exact generation model, date, settings, selected input and export receipt are
unknown. Project reference paths do not imply they were used as generation inputs.

Source: `assets/source/models/units/hobby-knight/original.glb` (4400204 bytes).
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/hobby-knight/v1/hobby-knight.glb` (4400276 bytes).
Source SHA-256: `a4d4c577786d9e5eefa4579505077f1b6f1cc1acb05efdda735fc8cfe35df3bf`.
Runtime SHA-256: `7c4679b7c42cb8e1d3720f552171f197df61eff2e3313ca63cbbad179009037a`.

One static glTF 2.0 identity root, mesh, primitive and opaque PBR material;
9,849 triangles. Three original embedded 2048x2048 textures (base color and
metallic/roughness JPEG, normal PNG), 64 MiB estimated RGBA plus mipmaps.
No skinning, animation, external resource or actual extension payload.
Original geometry and image bytes retained; unused exporter extension declarations
removed and deterministic source-hash metadata added. No simplification, texture
resizing, axis correction or deformation. Khronos source/runtime: zero errors,
one generated-tangent-space warning each; runtime-generated normal-map tangents
can vary between implementations. Source unused-extension info is removed.

Authored height 0.68; normalized X/Y/Z 0.325913 / 0.680000 / 0.414977 Cells.
Measured radius 0.207796 / catalog maximum 0.4.
Low 7.5%-height contact radius 0.168170, base radius 0.31,
margin 0.141830 (target .04); compatible unchanged 1x1 footprint.
Wooden hobby horse extends forward but remains inside the authored base/model envelopes.
+Y up; +Z forward visually verified after positive-Z Move in restricted Edge: inspection-nSxCJC/move-positive-z-front.png. Minimum Y zero; runtime places contact at the procedural base top.

Rights reuse the Zombie/Troll paid-user basis, rechecked once for this batch on
2026-10-28: [Tripo terms](https://www.tripo3d.ai/terms), 2025-07-11, section 5.2.2;
[Pro pricing](https://www.tripo3d.ai/pricing). These support publication, distribution
and licensing, subject to the agreement and third-party rights. No separate
public asset license or exclusive copyright claim is invented. Details and
restrictions are preserved in provenance. Source remains outside published wwwroot.

See [the inbox workflow](../../../../import/models/README.md) and repository
`THREE_VIEWPORT.md` for gameplay screenshots, mixed performance and verification.
Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/hobby-knight/provenance.json --check`.
