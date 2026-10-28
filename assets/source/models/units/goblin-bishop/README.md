# goblin-bishop production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/goblin-bishop/original.glb` (4285600 bytes).
SHA-256: `b0deaffef6d346bc587edbc3ad62564c915660cf8b338f8f4c4a81754372646c`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/goblin-bishop/v1/goblin-bishop.glb` (3663172 bytes).
SHA-256: `66aa55ccfa75c812ca0c57e6ac0fd98664254aa20ba135b0c253811be6192b73`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9534 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.65, maximumRadius 0.34,
baseRadius 0.28; actual radius 0.170698,
contact radius 0.152019, contact margin 0.127981 Cells.
Unchanged logical 1x1 footprint. Oversized headgear and staff are intact; small body remains grounded.

+Y up; +Z forward verified after observed positive-Z Move in inspection-p9wJ6R/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-eh7OIo`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/goblin-bishop/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
