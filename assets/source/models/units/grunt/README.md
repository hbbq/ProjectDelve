# grunt production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/grunt/original.glb` (4957448 bytes).
SHA-256: `2a699e8f4843b845c05eba0d3b20145ce5ca5a64fa5ba19dc231265b9bc5adff`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/grunt/v1/grunt.glb` (3898076 bytes).
SHA-256: `21b813868094b194e0fffbc82dbaf7c2f0f31cb30ef10cec9c848c67ca57bf4e`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9413 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.78, maximumRadius 0.42,
baseRadius 0.34; actual radius 0.261202,
contact radius 0.222830, contact margin 0.117170 Cells.
Unchanged logical 1x1 footprint. Spear and shoulder armor remain intact; no footprint enlargement.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-of6lgA. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-of6lgA`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/grunt/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
