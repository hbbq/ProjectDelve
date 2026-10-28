# rogue production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/rogue/original.glb` (3694768 bytes).
SHA-256: `7b4f13dc66dbbc841388f7ee9ce3dacdf6b1409447524b7d353a78c6af89e9cc`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/rogue/v1/rogue.glb` (3255080 bytes).
SHA-256: `8418b1cec9db43a31ba0ea086ae3c101638b876302746f7bf9ba2ba76e64f5c0`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9724 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.73, maximumRadius 0.36,
baseRadius 0.3; actual radius 0.191604,
contact radius 0.157348, contact margin 0.142652 Cells.
Unchanged logical 1x1 footprint. Hood, cloak and narrow equipment remain intact at normal detail zoom.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-xJfZfd. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-xJfZfd`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/rogue/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
