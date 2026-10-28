# geomancer production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/geomancer/original.glb` (3979992 bytes).
SHA-256: `eb8123e6095d2453f568cc3ae2b6d0288c885f5a2de2ed80e5c52c40fd3049fe`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/geomancer/v1/geomancer.glb` (3559332 bytes).
SHA-256: `14d51830471f1dc932eb6ba99c2d682a9a888f93e617864792e8b8c8a5382ed6`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9064 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.75, maximumRadius 0.38,
baseRadius 0.31; actual radius 0.222927,
contact radius 0.178093, contact margin 0.131907 Cells.
Unchanged logical 1x1 footprint. Staff and blue crystals remain visible; narrow staff is not a picking surface.

+Y up; +Z forward verified after observed positive-Z Move in inspection-6A0Kot/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-1I9CIA`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/geomancer/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
