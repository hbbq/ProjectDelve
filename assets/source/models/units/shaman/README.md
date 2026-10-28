# shaman production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/shaman/original.glb` (4959892 bytes).
SHA-256: `66896c984c2ff230c4e4561941eabf8b991cb8b10dd9548d89661c545537554c`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/shaman/v1/shaman.glb` (4015216 bytes).
SHA-256: `18ea37c4d13eceedced67722f060187ed67a73535bc5a76f8847a0a8816b3980`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9401 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.68, maximumRadius 0.36,
baseRadius 0.29; actual radius 0.171510,
contact radius 0.145205, contact margin 0.144795 Cells.
Unchanged logical 1x1 footprint. Layered mantle and staff remain intact; small equipment loses detail at distant zoom.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-2Thlg9. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-2Thlg9`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/shaman/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
