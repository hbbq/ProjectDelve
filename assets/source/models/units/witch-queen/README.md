# witch-queen production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/witch-queen/original.glb` (6074996 bytes).
SHA-256: `e8d0414ba29e2223be7eaa6d2bbd2646022a1ccbebe3458d81c56784126e7103`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/witch-queen/v1/witch-queen.glb` (4572140 bytes).
SHA-256: `43e12ae377da42c7096c4bdb9580004f99376e29016d4436c56b08696f591621`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 7982 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.73, maximumRadius 0.4,
baseRadius 0.31; actual radius 0.240341,
contact radius 0.240341, contact margin 0.069659 Cells.
Unchanged logical 1x1 footprint. Crown and elaborate layered costume retained; 7,982 triangles is below the advisory 8,000 target.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-3YXsXO. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-3YXsXO`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/witch-queen/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
