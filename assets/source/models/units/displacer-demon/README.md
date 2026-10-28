# displacer-demon production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/displacer-demon/original.glb` (3664392 bytes).
SHA-256: `221ba5695916ee40efc465e81237a94fb20cb547f12d4f601113899fb0cbb1a3`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/displacer-demon/v1/displacer-demon.glb` (3345168 bytes).
SHA-256: `31ec2089567c47366a595351d14febfa55dbd9fbde257cb90c1ba5309120e7fc`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9621 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.98, maximumRadius 0.44,
baseRadius 0.38; actual radius 0.229132,
contact radius 0.210476, contact margin 0.169524 Cells.
Unchanged logical 1x1 footprint. Large curved horns, purple armor and cape retained; no radius conflict.

+Y up; +Z forward verified after observed positive-Z Move in inspection-Bjms5y/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-Mm2rUl`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/displacer-demon/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
