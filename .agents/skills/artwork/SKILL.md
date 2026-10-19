---
name: artwork
description: Discover, prepare, integrate and verify new or replacement ProjectDelve source artwork in Enhanced 2D using the existing pipeline. Use for /artwork or artwork integration requests, not image generation or game-content authoring.
---

# ProjectDelve artwork

Work from the repository root. Integrate originals under `assets/source/`, including
replacements at existing paths. Honor narrower invocation scope: audits do not
authorize exports.

## Inventory and review

Read `AGENTS.md`, `VISUAL_STYLE.md`, `ASSET_PROMPTS.md`, the artwork sections of
`README.md`, and `tools/prepare-artwork.py`. Inspect all source files recursively,
runtime files in `ProjectDelve.Web/wwwroot/art/`, and `assets/previews/`. Inspect
the pipeline's explicit `ASSETS` registrations and `EDGE_CROPS`, the current
`ProjectDelve.Web/wwwroot/board-visuals.js` catalog, its consumers in `app.js`,
`board-enhanced.css`, and geometry/fallbacks in `styles.css`.

Capture initial Git status and SHA-256 hashes of all originals before writing.
Include untracked and ignored source files, not just Git changes. For each source
record category, canonical ID, variant/direction, pipeline registration, runtime
path and catalog registration. Classify it as new/unregistered, replacement/stale,
integrated, or blocked with a reason. An exported file is not necessarily integrated.
List runtime assets whose source is missing separately; never delete them automatically.

Git changes help identify replacements, but a replacement may already be committed
while its runtime asset is stale. Do not infer freshness from timestamps or path
existence. For registered assets with uncertain freshness, review composition,
re-export through the existing pipeline and compare outputs. A full registered
catalog export covers committed replacements without a separate freshness database.

Decode originals fully; inspect dimensions, format, mode, alpha, EXIF orientation,
color profile and animation. Visually inspect candidates and existing counterparts:
metadata cannot prove safe composition.

## Map content and resolve ambiguity

Use existing mappings first, then filenames corroborated by canonical content:
`ProjectDelve.Engine/UnitContent.cs`, `UnitRoster.cs`, `Model.cs` (Terrain/Edge
kinds), `Machines.cs`, and `Resources.cs`, as relevant. For example,
`units/barbarian.png` maps to `unit["barbarian-type"]` and `edges/door-open.png`
to `edge.OpenDoor`. Hyphens/spaces are evidence, not permission to derive IDs
blindly by appending `-type` or changing case. Never invent content to match a file.

Recognize numbered terrain siblings such as `grass-01.png`, `grass-02.png`, etc.
Map the base to the canonical Terrain kind; follow Stone Floor with numeric suffix
ordering and paths such as `terrain/grass/01.webp`. Preserve existing slots/order
and gaps; flag duplicate/conflicting numbering. Do not rename/renumber originals.
Numbered Unit portraits do not establish a portrait variant convention. Directional
Rail uses explicit North/East/South/West overrides, never random rotations.

**Ask before any uncertain or destructive decision.** Name the exact files, explain
the evidence/conflict and offer the smallest relevant alternatives. Pause dependent
conversion/integration until answered. This includes unknown names, unclear IDs,
competing portraits, unexpected dimensions/aspect ratio/composition, destructive
cropping/distortion, missing rendering support, and conflicting documentation.
Larger square originals are explicitly allowed and need not match export size.
Do not weaken conversion assertions to bypass review. Independent clear files may
proceed only if their export does not touch blocked files. Never silently skip
ambiguous files and claim the whole batch succeeded.

Consult category-specific documentation and actual rendering requirements:

- Units currently export 512 x 512. The UI composes the circular clip, Side ring,
  name and HP; check focal details at token scale.
- Ordinary terrain currently exports opaque 256 x 256 full-bleed, top-down images.
  Preserve subdued texture, Cell coverage, grid and highlight readability.
- Edges currently export horizontal 512 x 80 transparent masters; only decoration
  rotates for vertical Edges. `EDGE_CROPS` is reviewed for the existing originals,
  not blanket approval for replacements. Recheck visible bounds, proportions and
  the transparent Open Door passage before reusing a crop.
- Machines, Resources and future categories follow this same workflow using their
  own documented dimensions, alpha, composition, variant conventions and consumers.
  Existing Machine/Resource image layers do not approve arbitrary sizing or glyph
  removal. Ask about missing support or unclear requirements, then implement only
  agreed presentation support. Do not force new categories into portrait/tile rules.

## Prepare and integrate

Originals are immutable: never edit, rename, move, delete or overwrite source files.
Do not modify game rules, scenarios or `CONSTRAINTS.md`. Do not introduce engine
state, legality or game content for artwork. Preserve unrelated user work.

Use normal sandbox execution for repository-local operations. Artwork preparation, local reads and tests do not inherently require elevated permissions. If the command runner fails during initialization (for example, `helper_unknown_error: setup refresh had errors`), treat this as an infrastructure blocker, not a permissions denial. Do not repeatedly request elevated execution to work around it; report the failure and stop when normal execution remains unavailable.

Batch related read-only inspections when practical. For substantial Python inspection prefer a small repository-local temporary script over fragile inline Python/PowerShell quoting, and remove only the temporary files created by this workflow after review.

Check `py -c "import PIL"` first per `AGENTS.md`. If unavailable, check installed
Python and an existing `.artwork-venv` for Pillow with WebP before considering
installation. Use the working interpreter to run `tools/prepare-artwork.py`.
Extend its explicit declarations only as needed for approved files. Do not create
a second converter, rules engine or speculative category framework.

Run the full pipeline, or `--edges-only` for an Edge-only batch. The full command
rewrites every registered export/preview: review all affected sources first. If
blocked files prevent that run, pause or add the smallest necessary selection
support to this pipeline with focused tests. Preserve sRGB/EXIF handling, Lanczos
resizing, encoding quality and lossless alpha verification. No destructive crop
or distortion without clarification.

Register runtime assets declaratively through stable content IDs in
`board-visuals.js`. Reuse existing variant patterns; keep catalog updates maintainable.
Preserve category defaults, per-content fallbacks, image-error behavior and Classic
View. Change CSS/consumers only as required, scoped to Enhanced 2D. Do not scatter
image paths into interactions. Preserve hit targets, legal-choice feedback, event
playback, selection, Side, Posture, nameplates, HP, Resources and Machine labels.

Leave `terrainVariantIndex`, its version/hash, seed derivation and lifecycle
unchanged; do not consume RNG. Replacements retain variant order/count. Adding
variants can change modulo assignments: report that consequence.

Regenerate relevant previews through the same pipeline. Currently the Edge preview
assumes the final three exports are Wall/Closed Door/Open Door, and the main sheet
assumes square non-Edge images. Adapt these assumptions only when actual added
assets require it. Update relevant README artwork inventory/specifications and
production notes without rewriting historical prompts or claiming browser approval.

## Verify and report

Inspect `ProjectDelve.Web.Tests/board-visuals.test.cjs`, `token-layout.test.cjs`,
`door-choice.test.cjs`, and `BoardAppearanceTests.cs`. Add focused behavioral
tests for supporting script/catalog/renderer changes where appropriate. Use
dedicated fixtures, never assertions about mutable real scenario configurations.
Avoid freezing total catalog size. When a new image invalidates a test that assumes
that content has no image, retain strict fallback coverage with test-owned missing
content instead.

Verify full runtime decoding, exact category dimensions, required alpha and color
profiles, and every catalog path/ID, including variants/directions. Use and extend
the pipeline's decode/alpha checks as needed; file headers alone are insufficient.
Compare all original hashes with the initial inventory, including blocked files.
Check deterministic variant vectors/reloads, unknown/custom-content and load-error
fallbacks, both Edge orientations, Open Door gaps and door-state playback changes.
Check token/name/HP/Posture, overlays, keyboard/pointer feedback and Classic.

Run focused checks first, then the suites and build from the root. When the running web app locks normal Debug output, use the verified repository-root `.artwork-qa` directory with `dotnet test ... --artifacts-path .artwork-qa` and `dotnet build ... --artifacts-path .artwork-qa`. Avoid deeper nested artifact directories: existing test fixtures rely on relative content-root resolution. **Run .NET tests and builds sequentially**, waiting for each process to finish before starting the next, to avoid competing output locks:

```powershell
node --test ProjectDelve.Web.Tests/board-visuals.test.cjs ProjectDelve.Web.Tests/token-layout.test.cjs ProjectDelve.Web.Tests/door-choice.test.cjs
dotnet test ProjectDelve.Web.Tests --filter FullyQualifiedName~BoardAppearanceTests
node --test ProjectDelve.Web.Tests/*.test.cjs
dotnet test ProjectDelve.sln
dotnet build ProjectDelve.sln
```

Review previews and the actual board at normal/minimum Cell sizes when browser
tools are available. Use `dotnet run --project ProjectDelve.Web -- --urls
http://localhost:5080` or an isolated host/session if a user's game is already
running; never replace that game for QA. Check both views, orientations, crowded
overlays and failed images. DOM tests/contact sheets are not browser visual approval.
If verification cannot run, state exactly what was attempted, the blocker and
remaining unverified behavior.

Report source-to-ID/runtime mappings, new versus replacement images, pipeline,
catalog, preview/documentation changes, check results, visual issues and every
blocked file/decision. Distinguish completed work from pending clarification and
explicitly confirm originals remained unchanged.
