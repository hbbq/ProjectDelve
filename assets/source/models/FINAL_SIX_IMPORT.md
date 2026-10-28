# Final six miniature review — 2026-10-28

> Current status: Red Dragon is now approved; all 32 Units have production miniatures. See the Red Dragon completion section at the end. The original investigation below is retained as history.

Five imports approved: **Royal Knight, Locomotive, Tower Troll, Ghost and Diamond
Kart**. Production total: **31/32**. **Red Dragon is deferred** and its untouched
source remains at `assets/import/models/red-dragon.glb`. No Engine or server
observation implementation files, scenario formats, rules, `CONSTRAINTS.md` or
runtime renderer implementation files changed. The original 26 source GLBs,
runtime GLBs and provenance files are byte-identical; their catalog rows and
descriptors are unchanged.

The complete read-only, before-mutation measurements are in
[final-six-preflight.json](final-six-preflight.json). Bounds come from every
transformed POSITION vertex, not accessor bounds or reference artwork. X/Y/Z
dimensions below use source axes; Royal Knight swaps X/Z in its prepared runtime.
All six source minimum Y values are exactly zero. Runtime ground-centers and
uniformly scales to `targetHeight`, standing at the procedural base top (.06
Cells above its Unit root). No geometry was scaled to a radius, stretched,
cropped, removed or deformed.

| Model | Source bytes | Triangles | Raw X × Y × Z | Normalized X × Y × Z | Height |
| --- | ---: | ---: | --- | --- | ---: |
| Royal Knight | 4,061,488 | 8,703 | .970917 × 1.000732 × .474731 | .824675 × .85 × .403226 | .85 |
| Locomotive | 3,387,980 | 9,415 | .387314 × .611755 × .996338 | .367209 × .58 × .944619 | .58 |
| Red Dragon | 3,668,624 | 9,852 | 1.003967 × .616669 × .443054 | 2.360672 × 1.45 × 1.041773 | 1.45 |
| Tower Troll | 4,754,456 | 9,759 | .618073 × 1.001007 × .357895 | .740941 × 1.20 × .429042 | 1.20 |
| Ghost | 5,282,096 | 9,448 | .581482 × .997894 × .582275 | .483648 × .83 × .484308 | .83 |
| Diamond Kart | 3,658,056 | 9,778 | .845154 × .703491 × 1.000031 | .636727 × .53 × .753408 | .53 |

Every source has **one mesh, one primitive, one material, three embedded textures**,
identity root transform (determinant 1), finite geometry/UVs/normals, one scene,
no animation, skinning, morph targets, cameras, lights or external resources.
Materials use opaque core PBR base color, metallic/roughness and normal maps;
no alpha/transmission/emissive effects. All sources have two 2048² JPEG maps and
one 2048² PNG normal map. All accepted runtimes have three 1024² PNG maps, retaining
all channels and bindings. Source declarations `KHR_materials_volume` and
`FB_ngon_encoding` are unused, with no extension payloads; preparation removes
only the declarations. No special Ghost shaders or vehicle presentation.

Khronos glTF Validator **2.0.0-dev.3.10**: all six originals and six diagnostic
prepared exports have **zero errors**, one existing generated tangent-space
warning each. Source has one unsupported unused-extension info; runtime has
none. The warning concerns tangent-space portability between implementations.
The corrected Knight runtime was revalidated with the same result. Pillow fully
decoded all **36 original/prepared maps**; all are RGB. Every prepared map matches
the expected 1024² LANCZOS pixels. Above-8,000 triangle counts are advisory; all
are below the unchanged 12,000 hard limit and two-primitive limit.

| Model | Actual radius | Initial maximumRadius | Final maximumRadius | Contact radius | baseRadius | Base minus contact |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Royal Knight | .417159 | .44 | .44 | .228755 | .36 | .131245 |
| Locomotive | .484682 | .45 | **.485** | .472310 | .39 | −.082310 |
| Red Dragon (deferred) | 1.196189 | .88 | .88 | 1.196189 | .72 | −.476189 |
| Tower Troll | .374053 | .44 | .44 | .307478 | .43 | .122522 |
| Ghost | .273098 | .36 | .36 | .273098 | .30 | .026902 |
| Diamond Kart | .449056 | .45 | .45 | .403133 | .37 | −.033133 |

The existing contact estimator uses the lowest **7.5% of model height**, so it
includes low cloth, cowcatchers, tails and the curved sides of tires. It is not an
exact physical support polygon or a center-of-mass calculation. The preferred
.04-Cell radial margin is advisory. No base is enlarged just to clear it.

Original SHA-256, preserved before/after canonical move for accepted models:

| Model | SHA-256 |
| --- | --- |
| Royal Knight | `19dcdb2140479d3e38d098bd5ff40047df84d7bc380176087415b36ad488ce62` |
| Locomotive | `5450cf69f9276639ce4d8e080eeb6ee8360852dd4ce06cae7c112b4d9d889564` |
| Red Dragon | `2f180423d745036608c27224bf9e3aacae34a8a036a64a4e441cbf39a2237223` |
| Tower Troll | `68028188b198e463f18cf33bafb38bc138e4c058a2f10873b74b1a3700a8dfdc` |
| Ghost | `0466e6c939cd6a15d9657d0d44004febe4e72f846f1eb486d6ea03ca53426fa0` |
| Diamond Kart | `ff1dac86418b6ecbe4e06a8509355707d76b3fae7500e70d62091e03530cfe67` |

## Model findings and adjustments

**Royal Knight:** real horse, rider, cape/plume and rearing pose retained. Rear
hooves are on the base and provide visually plausible support; no physical
balance/center-of-mass claim is made. Head/lifted forelegs overhang the base but
remain inside .44. The outermost vertex is at source-axis normalized
(−.412338,.338819,−.063241), radius .417159. Plume remains within the same envelope.
Source is **−X forward, +Y up**, proven by the initial sideways positive-Z Move.
A **rigid +90° Y scene wrapper** corrects the prepared GLB to +Z forward. Runtime
dimensions become **.403226 × .85 × .824675**; every geometry buffer, original node,
material and authored transform is preserved. Radius/height are unchanged.
Corrected movement and base support were inspected in production Edge.

**Locomotive:** boiler, cab, chimney, cowcatcher and wheels retained. Length
.944619 Cells is close to a full Cell and visibly exceeds its circular base.
The largest vertex radius comes from the **rear cab corner** at
(−.164364,.188595,−.455962), not a malformed wheel. Low cowcatcher extends to
Z=.472310. The lowest .005-Cell band radius is .279421, inside the .39 base;
multi-angle views confirm wheels look supported while cab/cowcatcher overhang.
**maximumRadius .45 → .485**, a .035-Cell increase, is the only catalog dimension
change in this batch. No shrinking or wheel distortion. +Y up/+Z forward verified.

.485 is below half a Cell and the existing generic .92 limit. It fits the current
nine-column transient FoW pool at every yaw. Actual model/base radius already feeds
clipping. Public camera bounds already account for registered heights/radii; this
change does not exceed the preexisting Troll overhang allowance. Logical footprint,
base and .46-Cell picking proxy are unchanged. Long vehicle ends may visually
approach neighbors at diagonal yaw; they do not intercept neighboring legal Cells.

**Tower Troll:** the complete tower costume and battlements are retained. Its
.740941-Cell width and .307478 contact radius fit the existing .43 base; feet look
grounded and tower geometry needs no modification. +Y up/+Z forward verified.

**Ghost:** opaque pale blue-gray spectral cloth, torn hem and narrow silhouette
remain intact in both lighting modes and from rear/side. No transparency, glow or
emissive additions. Radius .273098 fits .30 base; .026902 margin misses preferred
.04 by .013098, advisory only. +Y up/+Z forward verified.

**Diamond Kart:** four tires, driver, hood diamond and rear engine retained.
Radius .449056 fits .45 by .000944; no extra rounding slack is invented. Low-band
contact estimate .403133 exceeds .37 base by .033133; the actual lowest .005-Cell
band radius .343526 is inside the base. Viewed tire contacts look supported on the
circular base, with tire/chassis overhang. +Y up/+Z forward verified. No baseless
presentation or distortion. Canonical scenario fixtures use Asphalt; simple
Move/Attack/picking fixtures use explicit test-owned stats. No production terrain
restriction or content was modified to make QA pass.

**Red Dragon — deferred:** retains head, two wings, tail and four legs. The
farthest point is low tail geometry at normalized source-axis
(−1.180336,.019159,−.194103), radius **1.196189**. 962 source POSITION entries
exceed the configured .88 radius, including the longitudinal silhouette; this is
not a single stray vertex. Overall X extent **2.360672** exceeds the logical 2×2
span, while wings and tail add diagonal overlap risk. At target height **1.45**,
the model exceeds the .92 generic ceiling by **.276189** (30.0% of that ceiling).
Rotation cannot reduce radial extent. Fitting .92 by uniform height adjustment
would reduce height to about **1.115**, a substantive **23.1%** shrink; not applied.
Changing metadata to 1.197 alone would fail the existing schema/runtime bound.

The actual canonical Type remains **2×2**, not four 1×1 copies. An isolated
**diagnostic-only** preview at height 1.0/radius .826 used that actual canonical
footprint and .72 circular base to identify anatomy and tail contribution. It was
never a production registration or a target-height approval; no source bytes were
edited. Orientation appears longitudinal along X, +Y up; final +Z semantic facing
needs review if integrated later. Camera/FoW/picking approval at authored height
is **not claimed**. At radius >1, a moving silhouette can intersect more than three
Cell columns per axis, exceeding the current fixed 3×3 FoW copy guarantee;
elevated wings/tail therefore cannot be certified with the present bounded path.
Camera overhang would also require an approved larger descriptor; logical picking
must continue using the authoritative 2×2 footprint. Future choices are an
explicitly redesigned radius/clipping/camera budget, an explicitly approved lower
presentation height, or a newly authored compact pose. None is silently selected.

## Assets and provenance

Each accepted original is at `assets/source/models/units/<slug>/original.glb`;
runtime is `ProjectDelve.Web/wwwroot/art/models/units/<slug>/v1/<slug>.glb`.
Each has a source README and provenance JSON with exact paths/hashes/bytes,
references, original inspection, oriented runtime inspection and compatibility.
Catalog IDs are `<slug>/v1`, version `tripo-v1`, approved, max 12,000 triangles,
two primitives and 1024 textures. Normal importer exclusive writes were used.
Re-running the inbox reports only the deferred dragon and changes no files.

| Model | Runtime bytes | Runtime SHA-256 |
| --- | ---: | --- |
| Royal Knight | 3,668,588 | `64c5613981067d1a5bd9c2e49e6658f4ff4810c74c6a72cc3ca72a1566d78bea` |
| Locomotive | 3,462,924 | `4361700b9af3ab9aec79c5ebc84c1572f2a93eb5a249923b7120eace2f0d8f9e` |
| Tower Troll | 3,890,604 | `6440f641751a2b15e80cb687cbc7b546f82724f50bc0ce2bcdaad684d1821a7f` |
| Ghost | 3,930,992 | `b46acc5d04a6d79f2d0133e6357742558805ec51d46ef229df163d8e00779e43` |
| Diamond Kart | 3,448,648 | `20a684f1b7a1821560378d0a0a25c019c8663720637f5a29ec9c7691f286fbdb` |

Paid **Tripo Pro generation for these exact six files is owner-confirmed**.
Provenance reuses the existing paid-user distribution basis, with a shared check
of [Tripo terms](https://www.tripo3d.ai/terms) (still July 11 2025, 5.2.2) and
[Pro commercial-use pricing](https://www.tripo3d.ai/pricing). The existing
third-party-rights and usage restrictions remain recorded. Generation model/date,
settings, receipt, generation-time supplemental terms and actual selected inputs
are unknown. Recorded portraits/reference sheets are existing project references,
not invented proof of generation inputs or a new public asset license.

Python **3.14.7**, Pillow **13.0.0**, zlib **1.3.1.zlib-ng**, Node **v22.23.2**.
`prepare-unit-model.cjs` retains the existing texture preparation and adds only
optional reviewed `rotationYDegrees`; no renderer branches or dependencies.
Importer measures prepared bytes as well as sources and binds the rotation review
to hashes and angle. Omitted rotation preserves every prior export exactly.

## Restricted Edge verification

All captures were inspected as PNGs, including contact sheets. Production host
on **5081** used ordinary registrations after import. Existing **5080 PID 40312**
was preserved. Initial visual diagnosis used an ignored copy of wwwroot, exact
prepared candidates and the small Locomotive allowance; production checks used
the checked-in catalog/assets. No protected runner, execution rule, browser
profile, production preview feature or gameplay scenario was modified.

Reproduce data-only plans with `node tools/batch-model-qa-plans.cjs --final`.
Each call uses a fresh temporary headed Edge context through
`.codex/browser-qa.ps1`. Canonical detail/mixed/posture scenes preserve Types;
simple interaction fixtures deliberately override stats for repeatable decisions.

| Model | Four-direction shape | Positive-Z Move | Attack facing | Lying/restored | FoW hidden/entered/left | Canvas pick |
| --- | --- | --- | --- | --- | --- | --- |
| Royal Knight | Bcmty7 | YelSSF | dMyhmC | BDY0TP | pFmEOp | 4v2ZIa |
| Locomotive | aDbXvt | IK0uiE | gfnqVP | WkCUIu | S4BlGF | emYGBN |
| Tower Troll | 0gNCVW | AWPwT0 | vkLq6m | oM1Ys5 | lornm4 | dX4E0r |
| Ghost | xYiuuf | E5szOq | 42Z6mD | Vt8CdS | Gq0i5A | UEKADI |
| Diamond Kart | A1s3tr | fjhqcO | 8GBED6 | qoIPQu | NFrtfi | 1Ccpfl |

Entries are `inspection-` suffixes under ignored `.playwright-cli/browser-qa/`.
**3LIu03** covers ten figures, blue/red Side bases, perspective, orthographic,
overhead, angled/rear/side views, Lighting On/Off and 600×850 narrow layout.
**DdYme4** covers repeated Cardboard/Miniatures and Classic/Enhanced/Flat/Tabletop
switches and restart. **7PH3tJ/restored.png** confirms all ten models ready after
reload. **az9J21** adds fully loaded Lighting Off and narrow controls (Developer
drawer intentionally overlays the board there; 3LIu03 shows ordinary narrow UI).
No unintended silhouette or foot clipping, base penetration, unsupported rider, special
Ghost material or distorted wheels was found. Bases/Side indicators remain
distinct. Small or distant views naturally lose fine details and shorten labels.

Successful runs have no console errors or failed model requests. Superseded SSE
requests abort normally on refresh/scenario replacement. An initial canonical
kart fixture on StoneFloor was rejected; all relevant fixture terrain is now
Asphalt, without changing production rules. Its unsuccessful posture attempt was
discarded and replaced by qoIPQu. Dragon diagnostic-only anatomy capture: 7ekQXK.
Warm camera screenshots are qualitative views, not frame-exact orientation tests;
CPU tests assert transforms, picking and transient clipping independently.

## Bounded performance

Headed Edge, 1440×1000 viewport, 12×8 Asphalt test-owned board, five distinct
new models, Perspective 65°, Lighting On. Five figures (one per Type), then ten
(one per Side per Type). Reset timing after loading and eight alternating camera
rotations; 13/12 camera samples. Counts include board, bases and feedback.

| Figures | Draw calls | Scene triangles | Warm CPU render p95 ms | Frame interval p95 ms | Estimated textures MiB | Evidence |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 5 | 27 | 48,001 | 1.9 | 16.73 | 88.17 | Ae9Aet |
| 10 | 47 | 95,784 | 2.0 | 16.70 | 88.17 | az9J21 |

Camera-only CPU render p95: 1.9/2.3 ms; overall CPU medians 1.4/1.5 ms.
Lighting Off ten-figure snapshot has CPU render p95 2.3 ms, unchanged draws,
triangles and estimated texture bytes; programs 8 → 10. These are short CPU
submission and frame-interval samples, **not GPU timings, sustained FPS or
low-end hardware claims**. No excessive stress workload.

Both warm scenes have **28 uploaded textures, 16 uploaded geometries, 8 programs**;
scene material counts 46/50. Shared model cache: **5 geometries, 10 lit/Off
material variants, 15 textures**, one fetch per distinct URL independent of
instance count. Three 1024 RGBA maps plus mipmaps estimate **16 MiB/model**, or
**80 MiB** for these five. Other maps contribute about 8.17 MiB. This is
dimension-based memory estimation, not measured GPU allocation. All 31 models
loaded together would account for **496 MiB of model textures**, plus other maps;
catalog entries are not eagerly loaded. Existing URL/version caching/lifecycle
and disposal remain unchanged.

Five-figure fetch/decode/ready ranges: **112.8–115.7 / 299.1–401.7 /
414.8–514.7 ms**. Ten-figure ranges: **125.9–126.9 / 125.5–240.5 /
251.7–367.2 ms**. Localhost/browser cache and concurrent decode affect timings.
Exact diagnostics are saved with each capture; compact per-model timings are
in [final-six-performance.json](final-six-performance.json).

## Automated verification and remaining issues

Focused asset/import/catalog/viewport tests passed, including real geometry
sharing, grounding, yaw, both projection pick paths, Lying/restoration,
Lighting On/Off, discontinuous FoW movement and disposal for every new model.
Tests bind exact source/runtime hashes and dimensions, compare geometry buffers,
accessors, material bindings and original hierarchy, reproduce exports and verify
the reviewed rigid wrapper. Importer tests cover rotation review mismatch,
exclusive writes, repeated import and source preservation. Deferred dragon is
tested as unchanged, unregistered and rejected at its authored height. Prior 26
source/runtime/provenance hashes have strict regression coverage.

Full .NET regression: **1,601 Engine + 384 Web = 1,985 passed**, zero failed/skipped.
Full client/security suite: **598 passed**, zero failed/skipped; final focused
asset/importer rerun: **8 passed**.
Focused hosting/catalog: **7 passed**. Debug and Release solution builds:
**zero warnings/errors**. Actual Release fixture and ordinary publish passed;
ordinary output has **exactly 31 production GLBs**, exact provenance-bound hashes,
catalog and loader, no original sources or QA fixture copies.

Remaining incompatibility: Red Dragon at authored height. Contact-margin advisories
remain for Locomotive, Ghost and Kart and do not block approval. Existing
generated-tangent warning remains. Native touch, frame-exact transient FoW pixels,
measured GPU/driver allocations and reclamation, sustained/low-end performance
and formal extreme-zoom image-quality comparison are not claimed. No global
safety limit, gameplay behavior, Engine/server implementation, real/playtest
scenario or scenario format was changed.
## Red Dragon completion — 2026-10-28

**Current production coverage: 32/32 canonical Units.** This section supersedes
all Dragon deferral statements in the historical investigation below/above.
The original GLB and 1.45-Cell height are retained. All previous 31 originals,
runtimes, provenance files, model descriptors and Unit dimension rows match the
before-import snapshot exactly. Engine, server observation code, logical
footprints, authored scenarios and `CONSTRAINTS.md` were not changed.

Dragon uses a **1.20** model-radius budget (actual **1.1961894824**) and a **.90**
circular base. Full-height Edge inspection compared .85 support; .90 provides
additional toe support while respecting the 2x2 base limit. Feet and low geometry
still overhang; the 7.5%-height estimator requires 1.236189 including its advisory
.04 margin. This is not a physical balance or support-polygon certification.
No mesh was cropped, deformed or shrunk to fit a base.

The final runtime has a rigid **+90 degree Y** scene wrapper, verified against
actual positive-Z movement and Attack toward a neighboring target. An initial
-90 candidate faced backwards and was rejected. Original nodes, geometry buffers,
UVs and material bindings remain intact. Prepared dimensions are
**1.041773 x 1.45 x 2.360672 Cells**; all three textures are 1024 PNGs.
The historical farthest-point anatomy/orientation inference was insufficient;
production movement and attack views establish semantic facing.

- Original SHA-256: `2f180423d745036608c27224bf9e3aacae34a8a036a64a4e441cbf39a2237223`
- Runtime SHA-256: `1a5c4bba83ed707170952025e75007e30574203d93e9410d6f4e75da855f9f12`
- Runtime size: 3,413,616 bytes; 9,852 triangles; one primitive/material.
- Source: `assets/source/models/units/red-dragon/original.glb`.
- Runtime: `ProjectDelve.Web/wwwroot/art/models/units/red-dragon/v1/red-dragon.glb`.
- Khronos Validator 2.0.0-dev.3.10: zero source/runtime errors; the existing
  generated tangent-space warning remains. Export reproducibility tests pass.

Catalog and runtime descriptor validation now share footprint-aware limits:
**1x1 .92, 2x2 1.25**. Transient clipping retains nine copies below radius 1;
larger supported envelopes allocate sixteen shared-geometry copies, enough for
all four-by-four intersected columns. Only explored columns are enabled; vertical
planes clip every mesh and both lighting variants. Stable observable 2x2 figures
remain whole as required by existing observations. Hidden figures remain absent.
Picking uses the unchanged .46 * cellSpan proxy and authoritative occupied Cells.
Camera bounds use the existing public descriptor envelope.

Importer diagnostics now require explicit canonical Types from a saved public
`/api/designer/catalog` response through `--canonical-catalog`. They pass the
canonical span to base layout and enforce the appropriate model-radius limit.
Missing footprint data fails review instead of silently assuming 1x1. Focused
fixtures cover missing data, 1x1 rejection and the full .90 2x2 base allowance.

Final Edge evidence (`.playwright-cli/browser-qa/inspection-<id>`):

| Check | Capture |
| --- | --- |
| Full-height shape, base and four camera directions | EfSKMV |
| Positive-Z movement facing | ooRlCP |
| Attack facing | XDkPcR |
| Hidden, entered/whole partially observed footprint, then hidden again | 6JkrDK |
| Adjacent Barbarian/Troll/Goblin, projections, Lighting On/Off, narrow framing | Vb8nhJ |
| Canvas picking and 2x2 choices | EUh66C |
| Lying Cardboard and restored Upright | ZQvuSR |

Picking/posture captures preceded the final rigid orientation correction; their
geometry/radius/footprint contracts are unchanged and final CPU tests repeat them.
All successful runs have no console errors or failed model requests. Refresh
aborts superseded SSE requests normally. The first patrol fixture's nearby areas
could both be reached by one 2x2 footprint; the final fixture separates them and
runs three rounds to prove entry and exit. No gameplay change was needed.
Data-only plans with dedicated scenarios are retained under
`ProjectDelve.Web.Tests/fixtures/miniatures/dragon-qa/`; copy a plan beneath
`.playwright-cli/` and use the dispatcher documented in `BROWSER_INSPECTION.md`.

Verification: **602 client/security tests**, **1,601 Engine tests + 384 Web tests**
passed, zero failed/skipped. Debug and Release builds succeeded with zero warnings
or errors. Release fixture publish and ordinary publish checks passed; ordinary
output contains **exactly 32 production GLBs**, exact provenance-bound hashes,
no originals and no QA fixture copies. The existing 5080 host was preserved.

Remaining limitations: advisory toe/low-silhouette overhang and generated tangent
warning; Lying intentionally uses the existing Cardboard representation. Normal
and narrow camera framing were inspected; manually excessive zoom can crop a
figure. Browser screenshots are not frame-exact transient pixel certification;
CPU tests verify every expected explored clipping column across positions/yaws,
both lighting modes and empty explored sets. Sustained GPU/low-end performance,
native touch and physical balance are not claimed.
