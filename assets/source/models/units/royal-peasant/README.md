# royal-peasant production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/royal-peasant/original.glb` (3803180 bytes).
SHA-256: `d5a97b08c16bd7d531341af66271c743271770cc4dc3fc7acf05fe2b37fbf920`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/royal-peasant/v1/royal-peasant.glb` (3250292 bytes).
SHA-256: `b866a71ed70e5f48801706c1339a9dcd119b18f5a2cca0529635fbdec714a27b`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9832 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.7, maximumRadius 0.36,
baseRadius 0.3; actual radius 0.152827,
contact radius 0.152827, contact margin 0.147173 Cells.
Unchanged logical 1x1 footprint. Helmet, striped clothing and spear retained; narrow figure has ample base margin.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-gTa02E. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-gTa02E`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/royal-peasant/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
