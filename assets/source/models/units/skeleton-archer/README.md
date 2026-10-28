# skeleton-archer production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/skeleton-archer/original.glb` (3639420 bytes).
SHA-256: `877e68d64ec4f08d9005facaef4bd0065dbfc88be8b8955153878e909da30b2c`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/skeleton-archer/v1/skeleton-archer.glb` (3356972 bytes).
SHA-256: `8f2379531e8c038506e11159500a1279b0264f2b79bb4702b0b9ad0a57a5c46f`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9484 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.73, maximumRadius 0.36,
baseRadius 0.29; actual radius 0.184746,
contact radius 0.146820, contact margin 0.143180 Cells.
Unchanged logical 1x1 footprint. Thin limbs, bow, quiver and cloak retained; bow silhouette remains visible from side/front.

+Y up; +Z forward verified after observed positive-Z Move in inspection-sk2qWh/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-azzpSn`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/skeleton-archer/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
