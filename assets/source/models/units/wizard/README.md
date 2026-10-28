# wizard production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/wizard/original.glb` (3999512 bytes).
SHA-256: `d7038de9039761cfbc85702c92907ed57fc5c7305101e434f28062028f8c9b4f`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/wizard/v1/wizard.glb` (3398252 bytes).
SHA-256: `d3a4a06332e6b5a5375b60b4a1b2fd4870a363f0631d4965ecbc3218183eb003`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9800 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.8, maximumRadius 0.38,
baseRadius 0.32; actual radius 0.210156,
contact radius 0.158561, contact margin 0.161439 Cells.
Unchanged logical 1x1 footprint. Hat, beard, staff and blue cloak remain distinct from front and side.

+Y up; glTF +Z forward convention retained, four-direction semantic scan in inspection-b25CVo. No axis correction; movement-facing interaction sampled on seven representative Types.
Four-direction Edge scan: `inspection-b25CVo`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/wizard/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
