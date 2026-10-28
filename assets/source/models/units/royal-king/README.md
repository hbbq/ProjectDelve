# royal-king production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/royal-king/original.glb` (4397552 bytes).
SHA-256: `d8cbf889b195ae87d0a931b972997e96b167434c913e60252e3cfd90270f2459`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/royal-king/v1/royal-king.glb` (3742112 bytes).
SHA-256: `6a368bdf63c00a6f33cb1313fbedb64abd3e6eac279a1a64d92a9cdd686254fc`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9982 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.83, maximumRadius 0.4,
baseRadius 0.32; actual radius 0.286929,
contact radius 0.286929, contact margin 0.033071 Cells.
Unchanged logical 1x1 footprint. Broad low cape accounts for the advisory contact margin: .033071 versus preferred .04, shortfall .006929. Actual radius .286929 fits .32 base and .40 hard model radius. No adjustment warranted.

+Y up; +Z forward verified after observed positive-Z Move in inspection-LGbapS/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-2hIpB7`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/royal-king/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
