# castle-guard production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/castle-guard/original.glb` (4985892 bytes).
SHA-256: `155f14a037396d544a7443f9ac298b5b4ca1d608a0148ee2ef82bcdfbf819b6e`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/castle-guard/v1/castle-guard.glb` (4098848 bytes).
SHA-256: `590ac0f822daaf3d0781282c66c43d7b24acb7d6a2d6b249ac3112b36e4efe28`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9486 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.85, maximumRadius 0.43,
baseRadius 0.34; actual radius 0.241564,
contact radius 0.222759, contact margin 0.117241 Cells.
Unchanged logical 1x1 footprint. Blue/gold armor, helmet and boots retained; ground contact and base fit are clear.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-dhOVEH. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-dhOVEH`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/castle-guard/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
