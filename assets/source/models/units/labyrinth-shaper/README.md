# labyrinth-shaper production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/labyrinth-shaper/original.glb` (3786568 bytes).
SHA-256: `4acd9b52548177401742eedc62065cec6f438d0a02aa470bd1d514baddc9df54`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/labyrinth-shaper/v1/labyrinth-shaper.glb` (3521404 bytes).
SHA-256: `68163bdb655b597f3fe5238ff574f10aa2774ef8ab94e9ad02220ac68c06a56f`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9386 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.85, maximumRadius 0.4,
baseRadius 0.33; actual radius 0.223506,
contact radius 0.223506, contact margin 0.106494 Cells.
Unchanged logical 1x1 footprint. Large stone shoulder pieces and hood retained within radius limits.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-bi15R0. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-bi15R0`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/labyrinth-shaper/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
