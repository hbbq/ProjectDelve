# goblin-peasant production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/goblin-peasant/original.glb` (4930644 bytes).
SHA-256: `bfe1dbf3ffd7a78dc61a21b2488d69cdde4c6bdc90c361ddedce178d6331b529`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/goblin-peasant/v1/goblin-peasant.glb` (4037536 bytes).
SHA-256: `c6262367e6148b6dde5c491c98885d6f69490b443859feadd3099a339dee4676`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9482 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.55, maximumRadius 0.32,
baseRadius 0.27; actual radius 0.136749,
contact radius 0.132775, contact margin 0.137225 Cells.
Unchanged logical 1x1 footprint. Small spear-bearing silhouette and helmet retained.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-NFjjWJ. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-NFjjWJ`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/goblin-peasant/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
