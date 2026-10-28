# Zombie production miniature

The owner-supplied original.glb is Tripo H3.1 generated under a paid Pro plan.
It is preserved unchanged. provenance.json records hashes, inspection, owner
confirmation, unknown generation details, processing and rights review.

Reviewed terms (https://www.tripo3d.ai/terms), section 5.2.2, include paid-user
publication/distribution/licensing rights; Pro pricing includes commercial use.
These support game distribution, hosting and source/runtime repository tracking.
No standalone-output exclusion was found. Agreement and third-party-rights
conditions still apply. This does not invent an unrelated public asset license
or guarantee exclusive copyright. Generation-time supplemental terms/export
receipt were not supplied; paid generation is owner-confirmed.

Run:

    node tools/inspect-glb.cjs assets/source/models/units/zombie/original.glb
    node tools/prepare-unit-model.cjs assets/source/models/units/zombie/provenance.json
    node tools/prepare-unit-model.cjs assets/source/models/units/zombie/provenance.json --check

Runtime: ProjectDelve.Web/wwwroot/art/models/units/zombie/v1/zombie.glb.
The normal approved catalog registers zombie-type; no special flags are needed.
Both source and runtime may be version-controlled; only runtime is published.

Preparation removes unused extension declarations and adds source-hash metadata.
All 9,674 triangles, UVs and material bindings survive. The original keeps
three 2048-square maps; runtime uses three 1024-square lossless PNGs. One mesh,
primitive and material remain, without animation, skins, external resources or
base. At height 1.20 the radius is .325632, within the catalog .38 maximum.
Runtime ground-centers and scales uniformly. Three supplies the physical base
and independent Side rim. No destructive optimization or axis stretching.

For another approved model, create its source/provenance directory, inspect its
contents and rights, set source/runtime paths and reviewed hashes, run the same
preparation tool, then add a catalog asset and Type reference. Run model/catalog,
HTTP/publish, FoW and browser acceptance as described in THREE_VIEWPORT.md.

## Runtime texture standardization — 2026-10-28

Runtime: **3,789,900 bytes**, SHA-256
`07fb0b8d3fee6139503f0fd8df375c93f1e7bb335fa18589c76760a28b135e24`.
The source hash is unchanged. `preparation.textureLimit: 1024` drives the same
`prepare-unit-model.cjs` command above; `--check` reproduces these bytes using
Python 3.14.7, Pillow 13.0.0 and zlib 1.3.1.zlib-ng. All three embedded runtime
channels are 1024x1024; geometry, materials, orientation and visual dimensions
remain unchanged. These images are RGB and the material remains OPAQUE.
Khronos validation: zero errors, unchanged generated-tangent-space warning.
Estimated RGBA plus mipmaps: 64 to 16 MiB; this is not measured GPU allocation.
See `THREE_VIEWPORT.md`, "Runtime texture standardization to 1024", for batch
sizes, visual evidence, reproducibility and Release publish verification.
