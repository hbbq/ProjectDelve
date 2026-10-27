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
All 9,674 triangles and three embedded 2048-square textures survive. One mesh,
primitive and material remain, without animation, skins, external resources or
base. At height 1.20 the radius is .325632, within the catalog .38 maximum.
Runtime ground-centers and scales uniformly. Three supplies the physical base
and independent Side rim. No destructive optimization or axis stretching.

For another approved model, create its source/provenance directory, inspect its
contents and rights, set source/runtime paths and reviewed hashes, run the same
preparation tool, then add a catalog asset and Type reference. Run model/catalog,
HTTP/publish, FoW and browser acceptance as described in THREE_VIEWPORT.md.
