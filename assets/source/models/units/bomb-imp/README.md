# bomb-imp production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/bomb-imp/original.glb` (3959800 bytes).
SHA-256: `7152b2f58fb1e97300d82c1401012243a79c814856e6b81b4eb9b8dd97ade818`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/bomb-imp/v1/bomb-imp.glb` (3538080 bytes).
SHA-256: `9413ed0062edbc7925dcd667c23d014ee5a4ccd51ec384510fe30c4ec231e0fb`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9686 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.48, maximumRadius 0.28,
baseRadius 0.24; actual radius 0.140792,
contact radius 0.140792, contact margin 0.099208 Cells.
Unchanged logical 1x1 footprint. Small .48-height silhouette is legible when zoomed; bomb, horns and feet remain distinct.

+Y up; +Z forward verified after observed positive-Z Move in inspection-qg3wKF/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-eSegGY`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/bomb-imp/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
