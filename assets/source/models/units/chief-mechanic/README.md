# chief-mechanic production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/chief-mechanic/original.glb` (4118084 bytes).
SHA-256: `d69b60f2dff5eee26bb083a92a723f71e9da508e8875a09d5fac7aaec87e1a72`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/chief-mechanic/v1/chief-mechanic.glb` (3575316 bytes).
SHA-256: `d11b25c48b48aa2f2ac526e0e4501336deda5a55ab801f300e09192178d0da35`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9491 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.73, maximumRadius 0.4,
baseRadius 0.31; actual radius 0.166323,
contact radius 0.166323, contact margin 0.143677 Cells.
Unchanged logical 1x1 footprint. Apron and equipment silhouette retained; feet ground on the base.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-ulAWu9. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-ulAWu9`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/chief-mechanic/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
