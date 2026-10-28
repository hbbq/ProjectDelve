# royal-queen production miniature

Approved with the remaining 19-model batch on 2026-10-28 using the existing
manual importer and preparation tool. Paid Tripo Pro generation is owner-confirmed.
Generation model/version, date, settings, selected input and export receipt are
unknown. Reference paths do not assert generation inputs. Rights reuse the
established reviewed paid-user basis; see provenance for terms, restrictions and
references. No separate public asset license is asserted.

Original: `assets/source/models/units/royal-queen/original.glb` (5000436 bytes).
SHA-256: `c80b8a64e4f31ccb1965360aff7dd017ddfa2dcbf8ec2d14b91387efadd16e3a`.
Runtime: `ProjectDelve.Web/wwwroot/art/models/units/royal-queen/v1/royal-queen.glb` (4265064 bytes).
SHA-256: `d594d7514bc6551e9d784b578b4e53b67e51fe978040ba02756acdbed4441972`.

Original bytes and all three embedded 2048-square source maps are unchanged.
Runtime retains 9702 triangles, one mesh/primitive/material and
all geometry bytes. The three maps are resized to 1024 square using the generic
`--texture-limit 1024` option: LANCZOS, lossless PNG, aligned view repack.
Python 3.14.7, Pillow 13.0.0, zlib 1.3.1.zlib-ng. Estimated decoded runtime maps
use 16 MiB including mips (source-size equivalent 64 MiB).
Khronos source/runtime: zero errors, one generated-tangent-space warning each.
Unused source extension declarations are removed; generated tangents may vary
between implementations. Most batch models exceed the advisory 8,000 triangles
and preferred transfer budget while satisfying the hard production limits.

Unchanged targetHeight 0.83, maximumRadius 0.4,
baseRadius 0.32; actual radius 0.244687,
contact radius 0.244687, contact margin 0.075313 Cells.
Unchanged logical 1x1 footprint. Elaborate blue/gold costume, cape and crown retained; feet are on the base.

+Y up; +Z forward verified after observed positive-Z Move in inspection-vP8ZDw/move-positive-z-front.png. Runtime ground-centers at base top; no axis correction.
Four-direction Edge scan: `inspection-IcTk4Q`.
Further shared/representative gameplay, performance and verification evidence
is recorded in repository `THREE_VIEWPORT.md` and `provenance.json`.

Reproduce with `node tools/prepare-unit-model.cjs assets/source/models/units/royal-queen/provenance.json --check`.
Use the recorded encoder versions and set `PROJECTDELVE_PYTHON` to an installed
Python executable if needed. Source remains outside published wwwroot.
