# goblin-king production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/goblin-king/original.glb` (4626160 bytes).
SHA-256: `624e4403c252b26886127812c8c5cc320d49779c4bdb154b429e26603ad48918`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/goblin-king/v1/goblin-king.glb` (3959616 bytes).
SHA-256: `ecc660a87d9ac560c7da00ae041e540fe5752ae8fa068852ba56ece932946ee6`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9416 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.68, maximumRadius 0.35,
baseRadius 0.3; actual radius 0.232973,
contact radius 0.185995, contact margin 0.114005 Cells.
Unchanged logical 1x1 footprint. Crown, red cape and broad costume remain inside visual limits.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-HwtJEz. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-HwtJEz`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/goblin-king/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
