# Troll production miniature

The owner supplied `original.glb`, generated using Tripo under their paid Pro
plan. The GLB identifies its generator as Tripo but does not record an exact
generation model version. That version, generation date/settings, input selection,
and export receipt remain unknown. The original is preserved byte-for-byte.
`provenance.json` follows the Zombie format and records source inspection,
reviewed rights, reference paths, deterministic preparation, and visual envelope.

Project references (their exact use as generation inputs is not established):

- `assets/source/units/troll.png`
- `assets/source/units/troll/reference/character-sheet.png`
- `assets/source/units/troll/reference/full-body.png`

## Rights

Reviewed on 2026-10-27: [Tripo terms](https://www.tripo3d.ai/terms), last updated
2025-07-11, section 5.2.2 grants paid users publication, distribution and licensing
rights; [Pro pricing](https://www.tripo3d.ai/pricing) includes commercial use.
Together with the owner's paid-generation confirmation, these support commercial
game distribution, web GLB hosting and source/runtime repository inclusion,
consistent with Zombie. No standalone-output exclusion was found. Agreement,
applicable law and third-party-rights conditions apply, including the competing
generative-service restriction and Tripo's service-provision license. No separate
public asset license or exclusive copyright claim is invented. Generation-time
supplemental terms and an export receipt were not supplied.

## Source and runtime

| | Bytes | SHA-256 |
| --- | ---: | --- |
| Original | 4,370,304 | `fa1efdf52d87635dbce4fbf45784057d9dfb24b8fe88a90b518528f9f3788b33` |
| Runtime | 3,522,536 | `9bc1970d2ca0d51c3b14693b068c3b8f6f305a86df6e2a2e24ba7b8974f51274` |

Runtime: `ProjectDelve.Web/wwwroot/art/models/units/troll/v1/troll.glb`.
Catalog ID: `troll/v1`, version `tripo-v1`, Type `troll-type`.
The version identifies this repository export, not an inferred Tripo model version.

```text
node tools/inspect-glb.cjs assets/source/models/units/troll/original.glb
node tools/prepare-unit-model.cjs assets/source/models/units/troll/provenance.json
node tools/prepare-unit-model.cjs assets/source/models/units/troll/provenance.json --check
```

The inspection CLI defaults to 1.20 height. To reproduce the Troll measurements,
call the exported `inspect(file, 1.9)` function. Preparation uses the unchanged
reusable tool, removes only unused `KHR_materials_volume` and `FB_ngon_encoding`
declarations, and records the source hash. There are no actual extension payloads.
Geometry bytes, UVs and material bindings are retained. Embedded runtime maps
are resized from 2048x2048 to 1024x1024 with the existing texture-limit option.
There is no regeneration, simplification, axis correction or nonuniform scaling.

## Validation and dimensions

glTF 2.0; one default scene, one identity-transform root node, one mesh, one
triangle primitive, 9,504 triangles and one opaque PBR material. Three source
2048x2048 maps: JPEG base color (1,013,307 bytes), JPEG metallic/roughness
(720,723), PNG normal (2,310,267). Attributes: POSITION, NORMAL, TEXCOORD_0.
No external resources, animation, skin, morph, authored base, camera or light.
Finite transformed geometry, positive determinant 1 and valid indices/normals
pass runtime checks. All three images decode in Edge.

Original bounds: min `[-.433837890625, 0, -.2728118896484375]`, max
`[.433837890625, .991241455078125, .2728118896484375]`. Dimensions X/Y/Z:
`.86767578125 x .991241455078125 x .545623779296875`.
Uniform scale `1.9167882762230226` yields
`1.6631507651 x 1.90 x 1.0458452634` Cells, measured radius `.8550545154`.
Catalog height remains **1.90**, maximumRadius changes from `.44` to **.856**.
AABB centering and minimum-Y grounding are unchanged. +Y up and +Z forward were
verified after a canonical +Z Move in Edge
(`inspection-1Pvzxc/move-positive-z-front.png`); no axis correction is needed.
Feet are placed at the procedural base top (.06 Cell above the Unit root).
The wide pose can overhang that base; the base remains .46 radius.

Troll is 58.3% taller than the 1.20-Cell Zombie, and about 2.78 times its normalized
X width. The club, arms and broad stance make its silhouette wider than a Cell.
The user explicitly authorized a generic visual-envelope extension preserving
the intended height and proportions. Its logical footprint is still 1x1.

Khronos glTF validator `2.0.0-dev.3.10`: source and runtime each have **zero
errors** and one `MESH_PRIMITIVE_GENERATED_TANGENT_SPACE` warning. Tangents are
generated at rendering time; normal-map appearance can vary across implementations.
The source additionally has an informational message for the unsupported unused
`FB_ngon_encoding` declaration, removed in runtime. Full reports are local QA
artifacts `.playwright-cli/troll-{source,runtime}-validation.json`.
The 9,504 triangles exceed the advisory 8,000 target, within the 12,000 hard limit.
Runtime maps now use the standard 1024 limit, reducing estimated RGBA plus
mipmaps from 64 to 16 MiB. Geometry remains within the existing hard budget.

## Integration and verification

`THREE_VIEWPORT.md`, section "Production Troll", contains implementation details,
performance, screenshots, verification results and remaining QA limitations.
Upright Trolls use the ordinary approved cache; Lying, loading and failed models
use Cardboard. Zombie retains its H3.1 source and registration. No model-specific
renderer branches, Engine/server-observation implementation changes, logical
footprint changes or rules changes were made.

## Runtime texture standardization — 2026-10-28

Runtime: **3,522,536 bytes**, SHA-256
`9bc1970d2ca0d51c3b14693b068c3b8f6f305a86df6e2a2e24ba7b8974f51274`.
The source hash is unchanged. `preparation.textureLimit: 1024` drives the same
`prepare-unit-model.cjs` command above; `--check` reproduces these bytes using
Python 3.14.7, Pillow 13.0.0 and zlib 1.3.1.zlib-ng. All three embedded runtime
channels are 1024x1024; geometry, materials, orientation and visual dimensions
remain unchanged. These images are RGB and the material remains OPAQUE.
Khronos validation: zero errors, unchanged generated-tangent-space warning.
Estimated RGBA plus mipmaps: 64 to 16 MiB; this is not measured GPU allocation.
See `THREE_VIEWPORT.md`, "Runtime texture standardization to 1024", for batch
sizes, visual evidence, reproducibility and Release publish verification.
