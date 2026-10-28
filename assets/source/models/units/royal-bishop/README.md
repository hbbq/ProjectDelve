# royal-bishop production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/royal-bishop/original.glb` (4172584 bytes).
SHA-256: `e4ed4cecc95d038b4d4dda22a81cabd250a36c08da3382e9926bd9741c176192`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/royal-bishop/v1/royal-bishop.glb` (3691788 bytes).
SHA-256: `63d8d0a87be9788d4254194a58f08adbd7726cc1f3a3cb33e8d1dd0519ba47ed`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9476 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.85, maximumRadius 0.38,
baseRadius 0.31; actual radius 0.194069,
contact radius 0.165910, contact margin 0.144090 Cells.
Unchanged logical 1x1 footprint. Tall headgear, staff and blue/white robe retained; no base conflict.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-mrpRzU. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-mrpRzU`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/royal-bishop/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
