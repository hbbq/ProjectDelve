# Beastmaster miniature import — 2026-11-09

All six models were imported using the existing gated
`tools/import-unit-models.cjs` pipeline. Their canonical inbox names were
unambiguous. No importer, gameplay, footprint, portrait, full-body master,
character sheet or unrelated model was changed.

## Assets and sizing

Dimensions are in Cells, using the existing visual fields. Height excludes the
.06 procedural base. The radius budget is an envelope, not a second scale factor.
All six retain their canonical **1×1** gameplay footprint.

| Slug | UnitTypeId | Model ID | targetHeight | maximumRadius | Measured radius | baseRadius | Contact margin | Y correction |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| beastmaster | beastmaster-type | beastmaster/v1 | .80 | .30 | .19000 | .27 | .08745 | 0° |
| jackalope | jackalope-type | jackalope/v1 | .42 | .22 | .16732 | .20 | .08928 | +90° |
| giant-toad | giant-toad-type | giant-toad/v1 | .35 | .36 | .33051 | .39 | .08113 | 0° |
| dire-bear | dire-bear-type | dire-bear/v1 | .64 | .52 | .49817 | .43 | .06252 | 0° |
| raptor | raptor-type | raptor/v1 | .60 | .44 | .40854 | .37 | .06001 | +90° |
| light-elemental | light-elemental-type | light-elemental/v1 | .45 | .18 | .10905 | .17 | .06798 | 0° |

Beastmaster matches ordinary Hero height. Jackalope and Light Elemental are small;
Toad is low and broad. Bear's measured radius stays just below half a Cell, with
limited base overhang. Raptor retains its tail and stance. All contacts exceed
the preferred .04 margin and all bases fit the .45 cap. These are digital
presentation dimensions, not millimeter print dimensions.

For each slug:

- Preserved source: `assets/source/models/units/<slug>/original.glb`.
- Provenance: `assets/source/models/units/<slug>/provenance.json`.
- Full actual validator reports: `validation-source.json` and
  `validation-runtime.json` beside the original.
- Runtime: `ProjectDelve.Web/wwwroot/art/models/units/<slug>/v1/<slug>.glb`.
- Registration: `ProjectDelve.Web/wwwroot/art/models/unit-visuals.json`.

These files are intended for Git. The importer removed inbox copies only after
successful hash-verified preservation and registration. A before/after audit
preserved 503 pre-existing asset files (excluding the intentionally updated
importer README and catalog), including the six inbox sources at their archive
destinations. All previous model descriptors/dimensions and all 2D
source, reference and runtime artwork remain unchanged.

## Actual generation and rights evidence

Owner confirmation on 2026-11-09:

> Yes — all six were generated under my paid Tripo Pro plan.

> H3.1 for all except dire-bear that is h3.0. all are made today 2026-11-09

Records identify H3.1 for five and H3.0 for Dire Bear, generated 2026-11-09.
Exact build, generation/export settings, receipt and actual image input selection
were not supplied and remain unknown. Associated project portraits, approved
full-body masters and sheets are not represented as proof of actual Tripo input.

The current official [Tripo Terms](https://www.tripo3d.ai/terms), dated July 11,
2025, §5.2.2, and [Pro pricing](https://www.tripo3d.ai/pricing) were checked on
2026-11-09. The paid-user grant and Pro commercial-use statement support the
reviewed source/runtime repository and game distribution use, subject to the
agreement and third-party rights. Each record contains this batch's actual owner
evidence, hashes and review basis. No new public asset license was invented.

## Preparation and validation

Each original is a self-contained glTF 2 GLB: one mesh/primitive, one opaque
material, three embedded 2048 maps, finite positive transforms, no external
resources, animations or skins.

| Slug | Triangles | Original bytes | Runtime bytes |
| --- | ---: | ---: | ---: |
| beastmaster | 9,322 | 3,561,020 | 3,187,600 |
| jackalope | 9,774 | 2,967,064 | 2,745,128 |
| giant-toad | 9,426 | 3,658,432 | 3,176,988 |
| dire-bear | 9,885 | 2,828,456 | 2,609,776 |
| raptor | 9,596 | 4,331,404 | 3,492,108 |
| light-elemental | 9,910 | 2,860,900 | 2,437,852 |

All fit the 12,000-triangle/two-primitive production contract. All exceed the
advisory 8,000 recommendation; no remeshing was performed. The established
exporter fully decoded maps, downsampled to 1024 with Pillow LANCZOS and repacked
lossless PNGs. Toolchain: Node 22.23.2, Python 3.14.7, Pillow 13.0.0,
zlib 1.3.1.zlib-ng.

Jackalope and Raptor arrived -X-forward. A supported rigid +90° Y wrapper corrects
them to +Z, verified after confirmed positive-Z Moves. The other four already
face +Z. Original geometry, authored nodes, materials, UVs, shape and pose remain
intact. Runtime uniform height normalization centers XZ bounds and places minimum
Y at the procedural base top, following existing conventions.

Khronos glTF Validator **2.0.0-dev.3.10** checked all six sources and all six final
prepared exports: **zero errors**. Each has one generated-tangent-space warning
(normal maps without authored tangents, handled by the existing Three loader).
Each source also has one unsupported unused `FB_ngon_encoding` declaration info;
the existing preparer removes unused declarations. Each actual report and both
SHA-256 hashes are saved. Focused tests reproduce every runtime byte for byte and
verify unchanged source/geometry/materials/UVs/nodes, budgets and base contacts.

After a fresh `/api/designer/catalog` capture and actual reviewed provenance,
these read-only inspections passed:

```text
node tools/import-unit-models.cjs --canonical-catalog .playwright-cli/beastmaster-models/canonical-catalog.json --only beastmaster,giant-toad,dire-bear,light-elemental --texture-limit 1024
node tools/import-unit-models.cjs --canonical-catalog .playwright-cli/beastmaster-models/canonical-catalog.json --only jackalope,raptor --texture-limit 1024 --rotation-y 90
```

The identical commands with `--apply` passed every gate and imported all six.
They now intentionally refuse overwriting existing registrations. Reproduce using:

```text
node tools/prepare-unit-model.cjs assets/source/models/units/<slug>/provenance.json --check
```

Set `PROJECTDELVE_PYTHON` to an installed Python with Pillow if necessary.

## Light Elemental illumination

Only Light Elemental declares:

```json
"light": { "color": [1, 0.88, 0.60], "strength": 1.2, "radius": 2.25 }
```

`unit-visuals.js` validates/freezes this optional metadata.
`three-local-lighting.js` collects visible projected Unit roots, and
`three-viewport.js` combines them with existing Lava, Campfire and Loot sources.
It reuses the bounded field, attenuation, occlusion, brightness ceiling and
shader. There are no dynamic point lights, extra passes, GLB coupling or new
frame loop. It works with Cardboard/Miniatures in Tabletop 3D, matching existing
lighting scope. Lighting Off and Flat disable the shared uniform.

The displayed moving transient replaces its hidden stable root. Checkpoints,
summon/relocation, defeat/removal, cancellation and reload reconstruct sources
from observations. Rewind/fresh-load tests do not retain future positions or
introduce gameplay save state. Hidden Units have no projected roots;
Unknown-center transients emit nothing, even if a clipped sliver is visible.
Unknown receivers remain dark. No Vision, LOS, discovery, legality, phase or
Aura rule changed. See [LOCAL_LIGHTING.md](../../../LOCAL_LIGHTING.md).

## Verification and evidence

Protected Edge data-only QA used port 5081 and dedicated test-owned scenarios.
The existing port-5080 game and real authored scenarios were preserved. Initial
shape review used an isolated webroot; final checks served production assets.
Local ignored evidence: `.playwright-cli/browser-qa/inspection-<ID>/`.

| Check | Evidence |
| --- | --- |
| Six production models plus Barbarian/Goblin/Worker; desktop, 600×850 narrow, Cardboard/Miniatures | RrXq5b |
| Front/side/rear: Beastmaster, Jackalope, Toad, Bear, Raptor, Elemental | hfOK8F / tgNGDt / bEv3td / RRhplo / 71kGk5 / RrIxUr |
| Confirmed +Z Move, same character order | DhlYZb / c2Hitt / sYLiRv / 7zHfBM / UeEfGe / DDzSyV |
| Lying and actual wake/restored Upright, same order | qDqM7U / 9Q44vG / bvemjI / aoUKxZ / sP1srU / keM5JN |
| Actual canvas picking opens Beastmaster / Raptor card | 6eTjHa / YO3YXD |
| Elemental glow at rest, moved to (3,4), continued after page reload | TbDr4c |
| Actual Beastmaster summon: both models ready, one source | 3pJkJ1 |
| Defeat after Attack/Defence rolls: figure absent, zero sources | GOIQKx |
| FoW-hidden Elemental absent, no model request and zero sources | YCA2Zn |

Final successful checks had no console errors or failed asset requests. Early
refresh-based plans intentionally aborted an old SSE stream. Trial plans using
obsolete fixtures or incomplete choices were corrected and their incomplete
captures are not acceptance evidence.

Nine-model desktop/narrow sample: 47 draw calls, 89,881 triangles; one source,
40×28 field / 4,480 bytes, build 1.2 ms. Summon sample: two miniatures, 32×24 field
/ 3,072 bytes, build .5 ms, CPU render median/p95 2.1/5.8 ms. These are development
machine samples, not GPU timing or hardware-independent performance claims.

- Independent focused lighting/catalog/viewport: **172 passed**.
- New batch/base checks: **7 passed**, all six exports exactly reproduced.
- Full client plus browser dispatcher: **1,010 passed**.
- Focused .NET model hosting: **6 passed**.
- Full .NET: **2,679 Engine passed; 719 Web passed, 12 failed**. The same existing
  `ScenarioContentReferenceTests` fail on conflicting `knightly-boots` identities,
  documented before this import. No tests were weakened.
- Debug and Release builds passed. Release reports cached NU1900 vulnerability
  feed availability warning; no compilation errors.
- Release fixture and ordinary publish passed. Ordinary output contains exactly
  **38** provenance-bound production GLBs, no source/QA GLBs.
- Before/after hashes and `git diff --check` passed.

Save/continue and replay illumination are tested at the observation/checkpoint
boundary; browser covers shared current-game reload. No new save system or
dedicated replay UI was introduced. Native touch hardware, frame-exact transient
FoW browser pixels, GPU timings, sustained low-end performance and physical
printing were not measured. No import steps remain blocked.
