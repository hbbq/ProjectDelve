# Three.js Playtest viewport — Flat 2D and Tabletop 3D

Playtest offers Classic, Enhanced 2D, Three.js Flat 2D and Three.js Tabletop 3D under Tools → Display.
Enhanced remains the default. Renderer selection is local to the page; switching
and restarting do not create a different server session unless Restart itself is
requested. Designer and Generator preview continue using their existing renderers.

## Dependency preparation and publishing

The browser uses checked-in, local ES modules from
`ProjectDelve.Web/wwwroot/vendor/three/0.180.0/`. No CDN, bundler or WebGL
initialization is required for the DOM views. The directory includes the MIT
license, package identity, npm integrity and SHA-256 provenance manifest.

To reproduce or deliberately upgrade the dependency:

```text
npm ci --ignore-scripts --no-audit --no-fund
npm run vendor:three
npm run vendor:check
```

Use the installed Node runtime described in BROWSER_INSPECTION.md if the nvm shim
does not work in the sandbox. Network dependency restoration may need approval.
Normal .NET build/run/publish uses the prepared files and does not invoke npm.
After a version update, update the exact package version and lockfile, the import
in `three-viewport.js`, and verify the prepared module graph. The preparation
script rejects unregistered imports rather than copying only an entry file.
Versioned vendor URLs prevent mixed dependency versions. Application modules and
CSS use normal same-origin static-file revalidation; do not give their stable URLs
an immutable cache policy.

## Integration boundary

`app.js` owns transport, revisions, SSE queues, commands, decisions, cards, dice,
combat correlation and history. `playtest-viewport.js` owns presentation handoff
and pure interaction/LOD/interpolation helpers. Existing DOM board functions remain
in app.js behind a small adapter. The DOM board is kept current while hidden so
its existing choice references and fallback controls remain intact.

The Three viewport receives `displayedFrame`: the displayed observed state and
its matching cards, figure geometry, Machines, objectives and metadata. It never
reads Generator preview, Designer state or an internal engine state. Its interaction
view omits legal candidates while playback is busy; a received future decision
must never be rendered over an earlier frame.

The interface is `renderFrame`, `showInteraction`, `present`, `project`, `settle`,
`cancel`, `setActive`, `dispose`, plus the concrete creation/posture emphasis hook.
Camera notifications retain the shared playback's existing before/after ordering.
Three animation uses the shared pause/cancellation mechanism. RAF interpolates
visual positions; it does not advance rules, events or authoritative state.

Switch requests during a visual event wait for the matching authoritative
after-frame. They never call `cancelPlayback`, clear history or submit commands.
Only one Three instance/context is created per page. The dice/decision overlay is
the same node moved between sibling hosts. DOM camera controls remain their
original nodes; Three owns a separately bound control bank in the same location.
Failed activation rolls back the overlay and working viewport.

## Scene, artwork and adaptive information

One world unit is one Cell. X follows board x; Z follows board y; Y is visual
height. Cell `(x,y)` occupies `[x,x+1] × [y,y+1]`. A span-s figure is centered at
`(x+s/2, height, y+s/2)`. The orthographic camera looks down with north at screen
top in Flat mode. Tabletop adds tilt/yaw and selectable orthographic/perspective
projection. Tabletop adds optional directional/ambient illumination; Flat remains
unlit. There are no shadows or tone mapping. See Tabletop lighting V1 below.

Named groups separate terrain, grid, objects, rails, Edges, items, transient
movement, FoW, stable Units, decision feedback and Areas. Explicit height ordering
and depth-tested alpha materials keep decorative layers beneath gameplay marks.
Repeated terrain/FoW planes use material-based InstancedMesh batches after the
initial 50×50 browser sample demonstrated excessive per-Cell draw calls. Dynamic
objects remain ordinary keyed groups. The scene reconciles on observed frame
changes, not every animation frame.

`board-visuals.js` is the artwork catalog for both Enhanced and Three. Add a new
approved asset to the existing `tools/prepare-artwork.py` pipeline and catalog;
Three resolves it without another export pipeline. `terrainLayers`, variant,
rotation and Rail helpers retain their existing hashes, salts and supplied
`visualSeed`. Do not derive a seed from a partially observed board. Texture URLs
are resolved relative to the existing web root, cached once, marked sRGB and use
linear magnification/mipmapped minification. Quarter turns change object matrices,
never a shared texture. Prepared Rail SVGs are loaded as textures. Partial alpha
is preserved. Failed images retain category colors, glyphs or structural shapes.

Portraits, Side rings, active marks and broken Lying rings are world geometry.
Names/HP are small HTML labels, with fixed CSS fonts and ellipsis. Detail levels
start at projected diameters 28, 48 and 80 CSS pixels with 4px hysteresis. Labels
are culled offscreen and when crowded; focused information has priority. 1/1 HP is
omitted. Full information stays available through the Unit Card and logical Cell
focus. Transient movement has no HTML label, avoiding text above concealment.
Machine labels summarize ownership/storage at useful scales; hover and logical
focus expose the complete observed information.

## Camera and input

`three-camera.js` reuses camera-geometry.js and camera-observation.js without
importing board-camera.js. Full, Fixed and Auto preserve maximum-long-axis span,
active Human footprint and legal Move-placement priorities, capped overflow,
comfortable following and Own/All-observable policies. Auto expands without
oscillating smaller within an activation. Its local preference key is
`delve.three.camera.v1`; DOM preferences remain independent.

Center and logarithmic zoom interpolate over time and can be retargeted from the
current pose. Manual input suspends follow; Resume/Reset or the next appropriate
Human activation resumes it. Skip, reduced motion, session replacement and
deactivation settle/cancel local work without camera-only playback delays.

Drag pans; Alt+wheel and visible +/− controls zoom. Ordinary wheel scrolling and
Ctrl+wheel browser zoom are preserved. Touch supports drag/pinch; crossing a drag
threshold or changing pointer count suppresses gameplay tap. Arrow keys move a
logical Cell focus; Enter/Space chooses; Shift+arrows pans. Door choices also remain
available through the collapsible Board choices list, which submits the same
opaque candidates as picking. Multiple candidates use the shared chooser.

Picking raycasts the ground through the current camera matrix, then checks Cell,
footprint and enlarged Door proxies. Open Door transparency does not remove its
hit target. The shared client maps logical hits to supplied candidates in server
order. Placement/affected previews use projected Cells/IDs; no LOS, pathfinding,
attack-area or gameplay legality calculations live here.

## FoW, lifecycle and future modes

Unknown Cells are identified by the displayed Side's explored set before applying
sparse-known-terrain defaults. Hidden contents are absent from the server browser
projection. In Flat, Unknown covers conceal terrain/objects and transient movement.
Stable multi-Cell Units supplied as observable are above the cover in full.
Reach Area mission outlines may appear above Unknown Cells.

Movement uses each supplied segment independently, hides the stable figure, draws
a temporary world token below opaque Unknown covers, and reconciles the supplied
after-frame. Disconnected segments are never joined. Camera intent passes through
the existing observation-safe helpers, excluding hidden boundary padding.
No exploration, visibility or Phase rules are calculated in Three.

Rendering is invalidation-driven, with one RAF loop only while visual work is
active. Static/inactive views have no continuous render loop. Drawing buffers are
capped at DPR 2. Context loss stops frames and requests Enhanced fallback at the
next handoff checkpoint. Restoration permits reusing the same scene/context;
reactivation while still lost is rejected transactionally. Disposal releases
listeners, observers, geometries, materials and textures. Page-cache suspension
does not destroy the viewport.

Tabletop uses the same viewport, context, observed frames and interaction adapter.
Its concrete bodies and transient concealment are described below. No engine,
server observation or scenario serialization changes are involved.

## Tabletop presentation and physical components

The display selector's `three` and `tabletop` values both resolve to the one
lazy-created Three viewport. `createViewportRouter` commits representation changes
at authoritative event checkpoints, including switches within Three. It does not
cancel shared playback, restart a session or replace dice/card/history nodes.
`setPresentation` rebuilds decorative components, retains Unit roots and eases
component height while the camera transitions from its currently displayed pose.
Flat returns to its original overhead heights and artwork.

Camera pose and representation are independent. The Tabletop preset chooses
cardboard and 38° tilt/0° yaw; Overhead, Angled, rotation buttons and sliders then
change only camera pose. Resume preserves orientation; Reset restores the preset.
Tabletop-specific controls are hidden in Flat. Pan/zoom still suspend following.

`three-tabletop.js` contains the small physical visual factory. Each authoritative
Unit ID keeps a persistent scene root, observed footprint and information anchor.
Only its visual children change. The body supplies `infoAnchor` and `visualBounds`;
`updateLabels` projects those values. Static GLB bodies use this same boundary
without owning Unit identity, choices, cards, movement legality or state.
See Static miniatures V1 below for loading, approval and fallback.

The board is one neutral-sided slab: top Y=0, thickness .12 Cell and .12 Cell of
unprinted border. It is not a collection of extruded Cells. Existing material-
instanced terrain planes print the playable top; the border has no Cell/Edge hit
target. Artwork variants, quarter turns, Rail connections and supplied visualSeed
are unchanged; no combined map texture or new artwork export is used.

Cardboard bodies have radius `.46 * cellSpan`, 24-segment sides and .06 Cell
thickness independent of footprint span. Both faces retain Side color and identity.
The reverse uses the existing portrait, a subdued wash, broken rim and Lying mark.
Inactive back faces are omitted from draw submission; both faces participate in
turnover. Initial Lying figures immediately show the reverse. Only an observed
`PostureChanged` hook starts a turnover after its matching frame: 360 ms, 180°,
with world-Y compression limiting lift to .12 Cell. The root, footprint, active
mark and HTML information stay fixed. Skip/cancel settles the authoritative face.

Walls are .52 Cell high, .13 thick blocks on canonical Edge centers. Doors use
.60-high jambs and a .54-high leaf; opening lifts/shrinks the leaf out, leaving a
clear passage. Closing restores it. Windows retain a visibly open frame. Solid
Rock uses continuous low tops and observed boundary skirts (details below);
Trees/Fruit Trees use trunks, compact crowns and existing canopy images; Tables
use four legs, aprons and a distinct wooden top with the existing print. Machines
use compact blocks, artwork badges, owner
accents and projected storage labels. Static obstacle parts are instanced by
geometry/material. A separate `tabletop-shape-v1` variant domain varies rock
boundary tones without changing terrain artwork hashes.

Coal, Diamond, Banana, Fruit, Mushroom and Shell use shared low-poly geometry.
Unknown Resources fall back to a physical puck with catalog artwork/glyph.
Markers use thin discs with artwork/glyphs, including Banana Peel. Local item
roots remain inside their authoritative Cell and can grow briefly on appearance;
removals reconcile immediately. Resource transport is never inferred from Unit
capabilities. An observed item under a Unit also receives a small HTML indicator.

## Angled camera, interaction and concealment

`three-camera-geometry.js` owns Three-specific projection/inversion, ground
quadrilateral, projected-bound fitting and yaw interpolation. Shared DOM camera
geometry is unchanged. Tilt is limited to 0–55°. Full fits border and 1.5 Cells of
height with screen padding. Orthographic Fixed uses maximum board-space span
derived from the unprojected viewport corners; perspective uses the focus-plane
reference described below. Auto fits supplied active/Move footprints, margins
and nearby friends under the existing span cap; it rechecks bounds during camera
rotation. Angled pan bounds keep the focus on the board while allowing gutters:
requiring a rotated viewport to stay entirely inside the board would exclude
corner Units. Exact Flat retains its original clamping. Camera intentions still
come exclusively from camera-observation.js and the displayed observation.

Pan and anchored zoom use ground unprojection. Cell picking uses the real camera
ray and ground plane; token cap proxies map back to observed occupied Cells.
Door tolerance is measured against projected Edge segments in CSS pixels and
includes Open Doors. Supplied Position choices take priority over cap parallax.
Decorative meshes never intercept legal decisions. Feedback renders in a final,
depth-independent informational pass with the same renderer/camera so its Cell
alignment remains correct behind raised components. Cutaway obstacles temporarily
hides known obstacle/Machine decoration; it changes neither LOS nor observation.
Keyboard Cell focus and the accessible Board choices list remain available.

Unknown ground has a low opaque cover (.006 Cell). Hidden stable content is absent
from observed input and never constructed. Observable stable multi-Cell Units are
rendered whole, without Cell clipping, even over Unknown ground. Mission Areas
retain their established overlay behavior.

Elevated movement cannot rely on horizontal covers. Under FoW, Tabletop/angled
transients use `createMovementClipPool`: nine reusable shared-geometry copies,
each with four world-space vertical clipping planes bounding one intersecting
explored Cell. Every mesh material in each copy has clipping enabled. The pool
uses the currently displayed explored set, never a later frame. Hidden copies
are invisible, disconnected supplied segments are never joined, and transient
HTML labels remain absent. Material variants share textures and follow loading
state; only those variants are disposed with the pool. Camera following still
excludes hidden boundary padding. This is presentation masking, not another FoW
or LOS implementation.

## Tabletop lifecycle and verification

One invalidation-driven RAF covers camera, movement, turnover, Door motion,
creation and item effects; stable/inactive scenes are idle. Shared cached geometry,
materials and textures live until viewport disposal. Removed instanced batches
release instance resources; owned feedback materials and transient clipping
variants are disposed individually. Session replacement clears roots and effects;
deactivation/context loss settles and clears local motion. The same transactional
DOM fallback remains available after context loss. DPR stays capped at 2.

Run focused `three-viewport.test.cjs`, `three-camera-geometry.test.cjs` and
`door-choice.test.cjs`, then client/security suites and the solution build/tests.
The existing Flat assertions remain. Tabletop tests cover identity, dimensions,
all catalog pieces/fallbacks, rotated ray math, corner framing, clipping of every
transient child, whole observable 2×2 figures, leaf/turnover cancellation, context
recovery, texture failures, batching and idle rendering. `tabletop-scene.json`
and `tabletop-fog.json` are dedicated test-owned fixtures; the latter exposes just
one Dragon footprint Cell while projecting the complete figure.

### Tabletop Edge measurements — 2026-10-27

Protected dispatcher, isolated 5081 Edge, 1440×1000. Generated cases use seed
`0000000000000001`, E2/C2/D3/L3/OR2, with N2 for medium/30×20 and N3 for 50×50.
These are short local CPU/frame-interval samples, not GPU timers or general FPS
guarantees. Cold Tabletop first-frame includes reconciliation, shader preparation
and the next RAF after an already loaded Flat view. Startup spikes are excluded
from warm timings by Reset timing sample.

| Scene | Flat calls | Tabletop calls | Tabletop triangles | Textures / geometries | Estimated texture MiB | First Tabletop frame | Warm render p95 | Warm camera interval p95 / frames |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: | --- |
| Basic Combat, authored | 20 | 36 | 1,060 | 13 / 9 | 5.42 | 35.0 ms | — | — |
| 20×16, 6 rooms, revealed | 98 | 135 | 7,574 | 22 / 9 | 12.42 | 49.1 ms | 2.0 ms | 16.7 ms / 9 |
| 30×20, 12 rooms, FoW | 26 | 48 | 2,318 | 15 / 9 | 7.96 | 38.6 ms | 1.5 ms | 16.8 ms / 9 |
| 50×50, 24 rooms, FoW | 48 | 57 | 6,258 | 16 / 9 | 8.17 | 44.4 ms | 2.5 ms | 16.77 ms / 10 |
| 50×50, 24 rooms, revealed | 498 | 517 | 42,530 | 26 / 9 | 20.75 | 91.8 ms | 3.1 ms | 16.8 ms / 11 |

The revealed case initially submitted 1,259 Tabletop calls. Omitting settled hidden
token faces reduced that to 517; warm CPU render p95 changed from 3.8 to 3.1 ms.
Six further Flat/Tabletop pairs retained 26 textures, nine uploaded geometries,
nine programs and 20.75 MiB estimated textures. This establishes a bounded sample,
not a proof against every possible driver leak. Cold reconciliation p95 for that
run was 28.6 ms; initial Flat lazy-load first-frame was 183.1 ms.

Basic Combat's capture includes startup and was not reset for a warm sample.
The test-owned FoW patrol recorded 29 moving-token frames: CPU render p95 1.4 ms,
movement frame-interval p95 16.8 ms. Its 45 camera frames recorded CPU render p95
2.1 ms and interval p95 16.8 ms. Final verification passed 496 client/security
tests and 1,978 .NET tests (1,601 Engine / 377 Web); solution build had no warnings
or errors. No Engine or server-side observation implementation files changed.

Evidence under `.playwright-cli/browser-qa/`: `inspection-QHvp49` mixed physical
components/rotations/narrow (final capture `inspection-jqdH5H`);
`inspection-QI9yV6` partial 2×2 observation;
`inspection-d93tZi` entering/leaving FoW, restart/reload; `inspection-AQ8780`
Door/Move/Telekinesis/reverse-face sequence; `inspection-28WCQ7` manual combat
and view switches; `inspection-9XdQOS`, `inspection-SOMeEn`, `inspection-bqPGeO`,
`inspection-qbQBuI` generated measurements; `inspection-JF0oWG` switching counts.
`inspection-Zz8btA` is authored Basic Combat; `inspection-zTlKDA` records movement
timings. Node tests also protect Unit visibility throughout representation changes
and raised proxy picking at board boundaries.
Successful plans had no console errors or failed asset requests; refresh/reload
can abort the superseded SSE connection as expected.

Remaining hardware/tooling gaps: native canvas gestures, actual touch hardware,
real-driver context-loss injection and frame-exact fog-boundary pixel assertions.
Those math/lifecycle paths have focused automated coverage, but the data-only
dispatcher cannot inject them. Low-end/mobile GPU timing and actual VRAM were
not measured. Labels retain local collision suppression, cutaway is user-operated,
and simple component silhouettes deliberately do not imply gameplay height/LOS.

## Perspective camera and component refinement — 2026-10-27

Tabletop's Projection selector chooses Orthographic (default) or Perspective.
Lens selects vertical FOV from 20° to 80° in 5° steps, default 45°, and is disabled
in Orthographic. The math and transitions accept continuous FOV values. Projection
and lens share the existing `delve.three.camera.v1` preference record with camera
policy. Flat always returns to overhead orthographic, retaining the Tabletop lens
preference for the next switch. Classic, Enhanced, Designer and Generator use
their existing renderers.

The viewport owns one OrthographicCamera and one genuine PerspectiveCamera, with
one renderer/context. Only the currently displayed camera renders and raycasts.
Camera pose contains the board-space ground focus `(x,y)`, scale, tilt, yaw, FOV
and projection mix; it contains no Unit representation or gameplay state. Unit
roots, body bounds and information anchors remain independent of projection.

For viewport height H, vertical FOV F and mix m, focal length in CSS pixels is
`f = H / (2 * tan(F/2) * m)` and eye distance is `d = f / scale`. At m=0 the
orthographic camera is used exactly. With yaw-rotated coordinates u/v, height h
and tilt t, perspective depth is `d - sin(t)*v - cos(t)*h`; screen offsets are
`f*u/depth` and `f*(cos(t)*v - sin(t)*h)/depth`. Inverse ground/raised-plane
queries solve these same equations, verified against real Three matrices.
Every update derives distance from the pose; lens changes never accumulate dolly
offsets. Near clipping uses the nearest board/border/height bound, avoiding poor
depth precision on large boards. Far clipping includes the complete board depth.

Zoom expresses desired board-space span independently of the lens. Perspective
uses the matched orthographic ground coverage at the focus plane as that reference,
labelled "Cells focus span". Changing FOV derives a new distance while keeping
that focus scale/span; zoom retains the chosen FOV. Actual near/far ground coverage
changes with perspective strength, as it must under an angled lens. Tilt/yaw retain
focus and derive the scale for the requested span. Tabletop permits gutters even
at exactly overhead so crossing projection/orientation boundaries does not shift
a corner focus. Flat's clamping is unchanged.

Full solves the frustum inequalities at all board/border corners at heights 0
and 1.5 Cells (the Miniatures V1 envelope), with 28px padding. It does not assume bounds vary linearly with
distance. Auto uses supplied active and legal Move footprints first, then margin
and nearby friends. Perspective fitting accounts for actual vertex depth and
16px padding. Comfortable following tests projected bounds and searches along
the focus translation. Within-activation expansion remains stable. During lens
and rotation transitions Auto rechecks the active footprint; optional regions
can overflow at the span cap. If even the active footprint cannot fit under that
cap on a narrow viewport, its complete display takes priority and the view widens.

Transitions use the existing invalidation-driven RAF, 260ms cubic easing, shortest
yaw and logarithmic span interpolation. FOV and projection strength interpolate
continuously; distance and compensated scale are recalculated at each displayed
pose. Intermediate projection uses a real perspective lens tending to parallel
rays, without a second render pass/context or projection-matrix blend. Retargeting
starts at the displayed pose. Manual input interrupts following/position movement
immediately while an unfinished lens request completes smoothly from its displayed
value. Skip Effects, reduced motion, deactivation and session replacement settle
local work without adding playback pauses. Idle scenes still schedule no RAF.

Picking uses the active camera matrix. Raised closed-Door proxies use the actual
projected four-corner polygon, including its perspective taper; Open Door proxies
retain their ground Edge tolerance. Unit caps map back to observed footprints;
supplied Position choices retain priority. Ground rays above the horizon return
no hit. HTML information anchors, sampled token sizes, logical focus, Areas and
objective indicators project through the same pose; Unit labels behind the eye
are culled. Decoration never supplies legality.

FoW remains the server's displayed observed projection. Unknown terrain is checked
before sparse terrain defaults. Hidden stable content is never built. Stable
observable multi-Cell Units remain whole; transient copies retain the same four
vertical world-space clipping planes per explored Cell under both projections.
Hidden boundary movement still cannot drive the camera. No transient HTML labels
are created. No Engine, server observation, scenario format or rule changes are
involved.

Solid Rock now has coplanar, full-Cell tops at .22 Cell, reusing the original
variant/rotation artwork in a single opaque surface. Adjacent tops meet without
isolated columns or internal walls. A shared shallow, inward-faceted skirt appears
only where an observed Rock Cell borders a known non-Rock Cell; Unknown neighbours
never create an excavation boundary. Skirts stay inside the observed Cell and use
deterministic tones. No geometry merging/flood fill or hidden terrain lookup is
needed. Shared geometry is cached once; keyed instanced batches are retained until
their observed terrain-derived transforms change. Reconciliation computes these
transforms on observed frame changes, never on camera RAFs. Single-surface tops
also avoid perspective depth striping from near-coplanar fallback/artwork layers.

Tables have an .80×.55 Cell horizontal wooden top at .40 Cell, four .085-wide legs,
and two aprons. The existing print follows the complete prop's deterministic
quarter turn. Solid wood supplies the loading/failure fallback. Geometry/materials
remain shared and static parts enter the existing instance batches. Neither prop
changes logical placement, movement, LOS, height or collision rules.

Known presentation limits/defaults: at a ground horizon, visible ground span is
infinite. Fixed's focus-span label is a finite framing reference, not a literal
bound on every perspective corner ray. Matching those farthest intersections
instead would cause strong apparent zoom at the focus when changing FOV; the
focus-plane reference is the chosen stable presentation default. Coordinate
enumeration uses a finite overhead reference at the horizon; actual picking still
rejects sky rays. The low Rock silhouette remains available for later visual
tuning. Local label collision suppression and
the fixed 1.5 Cell Tabletop height envelope remain. Future taller approved bodies
must extend that presentation budget without gameplay changes.

### Refined camera/component verification

Final client/security verification: 504 tests passed, including original
orthographic assertions and new real-matrix perspective/inverse tests, continuous
FOV compensation, Fixed coverage, Full/Auto depth fitting, rotated/narrow views,
focus retention, retargeting, interrupted lens zoom, touch tap suppression, Door
and Unit proxies, hidden camera-event suppression, FoW clipping across projection/
FOV/yaw combinations, observed Rock boundaries, four-leg Tables, instance reuse,
reduced motion, Skip/settle, replacement, context recovery and idle rendering.
The .NET solution build has zero warnings/errors; the complete regression suite
passed 1,978 tests (1,601 Engine / 377 Web).

Protected Edge dispatcher, isolated 5081, desktop 1440×1000 and narrow 600×850.
Generated 50×50 comparisons retain the previous seed/profile. These are short
local CPU samples, not GPU timer or VRAM measurements. First-frame values below
are direct Tabletop activation, including lazy scene setup; the older record used
Tabletop after preloading Flat, so those startup values are not comparable.

| Scene | Calls | Triangles | Textures / geometries | Direct Tabletop first frame | Warm CPU render p95 | Camera CPU p95 / interval p95 / samples |
| --- | ---: | ---: | --- | ---: | ---: | --- |
| 50×50, 24 rooms, revealed | 476 | 23,340 | 29 / 7 | 153.9 ms | 3.4 ms | 2.9 ms / 16.73 ms / 7 |
| 50×50, 24 rooms, FoW | 52 | 6,018 | 16 / 7 | 131.5 ms | 2.5 ms | 2.4 ms / 16.7 ms / 7 |
| 50×50 test board, 2,499 Rock Cells | 18 | 10,992 | 6 / 7 | 130.9 ms | 2.9 ms | 1.7 ms / 16.7 ms / 8 |

The previous revealed Tabletop record had 517 calls, 42,530 triangles and nine
uploaded geometries. The refined sample reduces calls about 8% and triangles
about 45%; estimated textures remain 20.75 MiB (FoW 8.17, Rock test 2.83).
Cold CPU render spikes are excluded from warm samples. Repeated projection
switching retained 29 textures, seven geometries and ten programs across six
fully settled projection changes in the sampled scene; source meshes
used to build static instances do not register retained artwork fallbacks.

Evidence under `.playwright-cli/browser-qa/`:

- `inspection-wO7DWU`: 20°/45°/80° and orthographic initial comparison.
- `inspection-0yATp5`: final Fixed lens comparison, rotated and narrow Full.
- `inspection-stdXXo`: final revealed generated Rock geometry and warm timings.
- `inspection-yZcZp9`: final dense FoW performance.
- `inspection-33CdG4`: continuous 50×50 Rock surface and timings after depth fix.
- `inspection-CJY2o1`: 80° FoW entry/exit and restart.
- `inspection-GBMfI9`: Door opening, legal Move and observed posture playback.
- `inspection-7UdQiv`: Attack/Defence and renderer switching.
- `inspection-FKh27y`: settled Flat, Classic, Enhanced, Tabletop, restart and reload.
- `inspection-7hl1Hi`: six settled projection switches and stable resource counts.
- `inspection-TmuUd8`: Table close-up, angled/rotated and overhead.

Successful plans reported no console errors or missing assets. Refresh/replacement
plans include expected aborted superseded SSE requests. Native canvas gestures,
real touch hardware, real-driver context loss, frame-exact FoW pixel assertions
and long-duration/GPU performance remain manual QA gaps: the restricted dispatcher
cannot inject those operations. Node tests cover the corresponding math/input/
lifecycle paths. No browser permissions or protected dispatcher files changed.

## Tabletop lighting V1

`three-lighting.js` owns one world-space `DirectionalLight` and neutral
`AmbientLight` in the existing scene. Tabletop enables the rig by default; Flat
always hides it and uses its original Basic materials. The renderer, context,
color space, tone mapping and shadow-map settings are unchanged. There are no
shadow maps, local lights, postprocessing, emissive effects or animation loop for
lighting. The module receives a scene, control host and invalidation callback;
it has no observed-state, camera, session, rules or transport input.

Defaults are key intensity **0.85**, color **#fff5e8**, ambient fill intensity
**0.65**, color **#ffffff**, azimuth **315 degrees**, elevation **50 degrees**.
The normalized lamp position is `(sin(a) cos(e), sin(e), -cos(a) cos(e))`, aimed at
the world origin. Zero azimuth is north (-Z), 90 is east (+X). It is a directional
source, so board size and camera pan/yaw/projection do not move the illumination.
Fill keeps the unlit faces readable; changing direction makes vertical faces and
faceted pieces visibly distinct without changing the printed board colors.

### Materials, Off and ownership

The physical factory explicitly requests lighting materials for slab sides,
cardboard sidewalls, Wall bodies, Door leaves/frames, Rock boundary skirts, Tree
trunks/crowns, Table bodies/legs, Machines and Resource pieces. Each authored
color/vertex-tone combination has one shared Basic/Lambert pair. Lambert ignores
the existing baked vertex colors to avoid double shading. Off selects Basic with
the original vertex tones (or the original plain color for Rock skirts). It also
hides the light rig, so no illuminated physical shaders are submitted in Off.
Both variants remain cached for quick comparisons; Off does not discard already
compiled programs. Stable pieces and instances never acquire per-object clones.

Terrain, raised Rock tops, portraits on both token faces, printed Side/Lying
indicators, prop prints, Marker art, fallback glyphs and informational highlights
remain `MeshBasicMaterial`. Textures still share the artwork catalog/cache, sRGB
handling, alpha, fallback and disposal paths. Lighting does not recolor a texture.
The factory retains existing simple geometry, including its cardboard sidewall;
V1 does not add bevel geometry or change approved artwork.

Switching On/Off remaps existing mesh material references, including instanced
meshes, without reconciliation, replacing Unit roots, resetting cameras or
interrupting playback. Canonical pair identity keeps static batch keys stable on
later observed frames. Only the transient FoW pool owns per-Cell clipping clones:
it prepares both physical variants with the same four vertical planes. Weak
variant registration does not retain completed movement. Pool disposal releases
both variants; viewport disposal releases cached pairs, lights and UI listeners.

### Controls and preferences

The camera/presentation control bank includes a compact, initially collapsed
**Lighting** disclosure, visible only in Tabletop. It offers On/Off, key/fill
intensity 0-2 in 0.05 steps, direction in 15-degree steps, and elevation 15-85
degrees in 5-degree steps. Other settings are disabled while Off. Labels wrap on
narrow screens. Preferences use `delve.three.lighting.v1`, independently of the
camera key; parsing, finite-number validation, clamping and unavailable storage
fallback are local. Renderer switching and restart retain them, and reload
restores them. Each actual change coalesces into one invalidation and returns to
idle when existing motion finishes. Reapplying identical settings does nothing.

Diagnostics add the current lighting settings, cached physical/total material
counts and `lighting.firstFrameMs`: time from a preference/presentation change
until its first CPU-submitted render, including the RAF wait. It is not GPU
completion time. Total material count covers the persistent caches, excluding
temporary clipping/feedback clones; geometry/texture/program counts come from
Three's renderer information. Programs are retained shader variants, not the
number of programs used by the current frame.

### Observation and FoW

The rig is unrelated to discovery and does not create content or light sources
from Units, terrain, resources or events. Existing displayed observations remain
the only content source. Unknown covers and grid remain unlit. No shadow or glow
can disclose a hidden object's presence. Observable stable multi-Cell Units stay
whole and unclipped over Unknown ground. Every elevated transient child retains
Cell-column clipping through On/Off changes, using the displayed explored set;
disconnected observed segments and observation-safe camera follow are unchanged.
There is no client exploration/LOS system and no Engine, server observation,
scenario-format, Designer, Generator or `CONSTRAINTS.md` change.

### Lighting measurements and visual verification — 2026-10-27

Headed Edge through the protected dispatcher on isolated 5081; 5080 was preserved.
Generated scenes use seed `0000000000000001`, E2/C2/D3/L3/OR2, N2 for 30x20 and N3
for 50x50, with 12/24 rooms respectively. Desktop viewport is 1440x1000. Timings
below are short local CPU submission samples after Reset timing sample, not GPU
timers, hardware benchmarks or a claim that lighting improves performance. Small
differences in either direction are within sample variability. Full-page captures
can temporarily resize an overflowing page and shorten camera animations; sample
counts and this limitation matter when comparing the following records.

| Orthographic scene | Calls Off / On | CPU render p95 Off / On | Camera CPU p95 Off / On | Camera interval p95 Off / On | Camera frames Off / On |
| --- | ---: | ---: | ---: | ---: | ---: |
| 30x20 FoW | 45 / 45 | 1.9 / 1.6 ms | 1.7 / 1.5 ms | 16.70 / 16.70 ms | 8 / 6 |
| 50x50 FoW | 52 / 52 | 2.3 / 2.6 ms | 1.5 / 2.4 ms | 16.73 / 16.70 ms | 4 / 8 |
| Dense revealed 50x50 | 476 / 476 | 3.5 / 3.7 ms | 3.2 / 3.1 ms | 16.70 / 16.80 ms | 7 / 8 |

Separate warm samples with the perspective lens already set to 65 degrees before
Reset timing sample (rather than mixing projection timings):

| Perspective scene | Calls Off / On | CPU render p95 Off / On | Camera CPU p95 Off / On | Camera interval p95 Off / On | Camera frames Off / On |
| --- | ---: | ---: | ---: | ---: | ---: |
| 30x20 FoW | 45 / 45 | 1.4 / 1.6 ms | 1.3 / 1.4 ms | 16.63 / 16.70 ms | 6 / 6 |
| 50x50 FoW | 52 / 52 | 1.9 / 1.9 ms | 1.9 / 1.7 ms | 16.70 / 16.80 ms | 8 / 8 |
| Dense revealed 50x50 | 476 / 476 | 3.5 / 3.7 ms | 3.2 / 3.5 ms | 16.70 / 16.70 ms | 7 / 8 |

| Scene | First default-On Tabletop frame | First Off change frame | Cached materials | Physical pair materials | Textures / geometries | Programs Off / On | Texture estimate |
| --- | ---: | ---: | ---: | ---: | --- | --- | ---: |
| 30x20 FoW | 94.7 ms | 13.9 ms | 48 | 16 | 15 / 7 | 10 / 8 | 7.96 MiB |
| 50x50 FoW | 124.7 ms | 17.3 ms | 53 | 18 | 16 / 7 | 13 / 10 | 8.17 MiB |
| Dense revealed 50x50 | 162.9 ms | 47.7 ms | 81 | 20 | 29 / 7 | 13 / 10 | 20.75 MiB |

The first-frame columns measure different operations: initial Tabletop entry
versus the first switch to Off after On. Off runs have already compiled the On
variants; their larger program counts are cache history, not additional active
lighting work. Initial viewport lazy-load times for Off/On runs were 194.6/188.6,
210.8/218.1 and 279.1/269.3 ms respectively. There is no added draw-call cost in
these samples and no significant warm CPU regression requiring optimization.

The dedicated FoW patrol measured 31/27 moving-token frames Off/On: movement CPU
render p95 1.7/1.3 ms, movement interval p95 16.8/16.9 ms, 22 settled draw calls,
32 cached materials, nine textures and six geometries. Soft key/fill 0.4/0.8 and
strong 1.5/0.3 in the mixed fixture retained 99 calls, 132 cached materials, 28
textures, 11 geometries and 10 programs; warm CPU p95 was 1.7/1.6 ms. These very
short intensity samples establish functional comparison, not relative speed.

Six additional Off/On pairs in the dense scene's default Auto framing retained
81 cached materials, 24 uploaded textures, seven geometries and 13 programs,
with 82 calls and 20.75 MiB estimated textures throughout. Cached textures can
exceed uploaded textures when outside the frustum. Warm CPU p95 across toggles
was 3.7 ms, and the last On change reached its first frame in 4.7 ms. In a separate
1440x1800 capture, where full-page screenshots did not resize the viewport, the
static render count stayed **4 before and after three captures**. Node tests also
verify one RAF per idle setting change, no RAF for no-op/inactive changes, stable
batch identity and disposal of both clipping/material variants.

Visual inspection found increased depth on Resources, Tree crowns, Table legs,
Wall sides and Door leaves, with unchanged printed terrain/portraits and readable
Side rings, Lying marks and choices. Comparisons include On/Off from the same
fixture/camera pose, 315/135/90-degree light directions, soft/strong settings,
orthographic and perspective FOV 45/65/80, 600px layout, FoW entry/exit and a whole
partially observed Dragon. Classic, Enhanced and Flat captures remain readable;
restart/reload restored Off and fill 0.80. Successful plans had no console errors
or missing assets. Refresh/import/restart plans can report expected aborted
superseded `/api/game/updates` SSE requests. One initial reused movement plan
encountered an automatically completed End Turn; rerunning with auto-choice
advancement disabled completed the same patrol in both lighting modes.
The final close-up comparison and 320px capture confirm lighting labels wrap
without horizontal control overflow. A reused Fixed-span selector was hidden
during close-up setup; the corrected plan used the visible zoom button.

Final focused tests passed throughout implementation; the complete Node
client/security suite passed **509 tests**. The cached solution build succeeded
with zero warnings/errors, followed by **1,978 .NET tests** (1,601 Engine and 377
Web). New focused coverage includes rig/configuration, corrupt/unavailable
storage and reload preferences, physical/printed materials, shared instances,
world-fixed direction, idle/no-op/inactive invalidation, renderer/projection and
session replacement, missing textures, both clipping variants' disposal, and
unchanged exploration/absence of unknown terrain. Existing Flat assertions were
retained. Tests use dedicated fixtures, never authored layouts as contracts.

Evidence under `.playwright-cli/browser-qa/`:

- `inspection-Vxi41L`: original unlit baseline, mixed components.
- `inspection-OFBulU`: same-pose Off/On, directions/intensity, 45/65 lenses, 600px.
- `inspection-utR906` / `inspection-Np057T`: 30x20 FoW Off/On.
- `inspection-fSamHD` / `inspection-pp4yA0`: 50x50 FoW Off/On.
- `inspection-qYMsSE` / `inspection-G5oXb3`: dense revealed 50x50 Off/On.
- `inspection-aHek5e`: six toggle pairs and bounded resource counts.
- `inspection-cak1sG` / `inspection-oxS6A2`: FoW movement timings Off/On.
- `inspection-T4gY9F`: stronger light, perspective FoW hidden/entered/left/restart.
- `inspection-r5O17P`: whole observable Dragon over Unknown, both modes.
- `inspection-BnRmEv`: soft/strong intensity samples.
- `inspection-rUXLJh`: Flat/Classic/Enhanced/Tabletop, restart/reload preferences.
- `inspection-Z2vbBi`: static rendering idle between captures.
- `inspection-C2pXzM` / `inspection-goaTFf`: perspective 30x20 FoW Off/On.
- `inspection-DKgIKd` / `inspection-gqwk7b`: perspective 50x50 FoW Off/On.
- `inspection-RIYQ1j` / `inspection-HQVBwB`: perspective dense 50x50 Off/On.
- `inspection-H0kIRN`: closer physical-pieces comparison and 320px controls.

Remaining gaps: native pointer/touch interaction, frame-exact transient FoW pixel
probes, real-driver context-loss injection, long-duration/low-end hardware and
GPU/VRAM measurements cannot be established by the restricted dispatcher. Node
tests cover input math, every transient child's clipping and lifecycle, but are
not a substitute for those native/hardware checks. No lighting implementation
issue remains known. Future emissive materials or local lamps can opt in at the
physical material factory/rig boundary after an observation-safe source contract
and light budget are defined; neither system is implemented in V1.

## Verification and diagnostics

Run focused `three-viewport.test.cjs` and `door-choice.test.cjs`, then the existing
Node client/security suites and .NET solution build/tests. Tests exercise real
Three scene graph/ray math with injected renderer/texture boundaries; they do not
pretend a Node fake renderer establishes WebGL fidelity. `ThreeViewportFixtureTests`
loads the dedicated mixed-artwork fixture in `fixtures/three-scene.json`.

Follow BROWSER_INSPECTION.md for isolated 5081 Edge captures; preserve port 5080.
Tools → Developer → Three.js diagnostics reports draw/texture/geometry counts,
first-frame latency from lazy load, CPU render timings and active-animation frame
intervals. Reset timing sample after textures settle. Texture MiB is an RGBA+mip
estimate, not measured driver VRAM; CPU submission time is not GPU frame time.
Capture sample counts and distinguish startup from warm measurements.

The protected dispatcher supports UI choices, screenshots and snapshots, including
the accessible Board choices list. Arbitrary canvas-coordinate gestures, keyboard
events, frame probes, request failure injection and context-loss injection are
not in its plan schema. Use focused automated tests for those paths and retain
manual hardware/browser verification gaps. Do not broaden its permissions.

Known limits: visual effects are intentionally simple; transient labels are omitted;
label collision avoidance is local; CSS material filters are approximated. Actual
touch hardware, lower-end mobile performance and injected real-driver context
recovery need manual checks. Existing authored scenario loading is exercised
without treating mutable layouts as test fixtures.

### Verification record — 2026-10-26

Headed Edge through the protected dispatcher, desktop 1440×1000 and narrow
600×850. These are local QA samples, not hardware-independent FPS guarantees.
First-frame latency includes the local lazy module load and scene setup, but does
not wait for every texture. Texture counts include generated fallback glyphs.

| Scene | First frame | Draw calls at capture | Textures | Estimated texture MiB |
| --- | ---: | ---: | ---: | ---: |
| Basic Combat, Full | 164.2 ms | 20 | 13 | 5.33 |
| Generated 30×20, FoW | 141.5 ms | 28 | 16 | 8.08 |
| Generated 50×50, FoW, 24 rooms | 146.0 ms | 48 | 16 | 8.08 |
| Generated 50×50, revealed, 24 rooms | 169.6 ms | 447 | 25 | 19.25 |

The generated samples use seed `0000000000000001`, E2/C2/N2/L3/OR2;
the 24-room samples use D3. The initial 8-room revealed 50×50 implementation
reported 1,310 draw calls in a zoomed view. Material-based terrain instancing
reduced the equivalent capture to 116 calls. A subsequent warm camera sample
reported CPU render p50/p95 1.4/1.8 ms and frame-interval p95 16.7 ms, with only
eight animation intervals; this is a short sample. Repeated switches retained
four geometries and 21 textures (13.75 MiB estimated) in the 8-room case.

The test-owned FoW patrol recorded 30 moving-token frames: CPU render p95 1.3 ms,
frame-interval p95 16.8 ms. Startup samples contain texture-upload spikes and must
not be compared directly with warm render timings. No GPU timer queries or actual
driver VRAM measurements were made.

Browser evidence is under `.playwright-cli/browser-qa/`:

- `inspection-xqQHce`: Enhanced/Three mixed-artwork comparison and narrow layout.
- `inspection-zXWfQe`: Stay, Attack, manual Attack/Defence dice and switching.
- `inspection-qjDDIZ`: patrol hidden/entered/left, restart and reload.
- `inspection-eJvlCW`, `inspection-iXM2D2`: 50×50 camera/switch measurements.
- `inspection-0lWEZZ`, `inspection-Zl7g3V`, `inspection-5c0s8j`: generated sizes/FoW/density.
- `inspection-P5TapN`: moving-token timings.
- `inspection-lTeWVg`: a submitted legal Move and subsequent observed playback
  (87 moving-token frames, CPU render p95 1.1 ms, frame-interval p95 16.8 ms).
- `inspection-cL9UVg`: corrected diagonal combat relationship during Defence.
- `inspection-ucLO4c`: small board and unchanged Designer/Generator interfaces.

Successful runs had no console errors or missing asset requests. Refresh/reload
FoW runs logged expected aborted SSE connections. Native canvas gestures, real
driver context loss and lower-end hardware remain manual verification gaps.

## Static miniatures V1

Tabletop now offers **Units: Cardboard / Miniatures**, independently of camera
and lighting. Cardboard is the default. `delve.three.units.v1` stores only this
local preference; invalid or unavailable storage falls back safely. Flat always
uses its original token presentation. Classic, Enhanced, Designer and Generator
rendering are unchanged.

The approved Tripo H3.1 Zombie and Tripo Troll, Goblin, Barbarian, Cleric,
Hobby Knight and Worker are registered in the normal production catalog.
Other Types retain Cardboard until approved assets exist. Test-owned procedural
figures remain isolated QA fixtures and never substitute for production artwork.

### Identity, bodies and safe replacement

`unitRoots` continues to own observed Unit ID, ground placement, complete
footprint, active mark and interaction/information association. Selection and
target feedback remain in the shared informational layer; Unit Cards remain in
the app. The small Tabletop body result is:

```js
{ group, facingNode, visualBounds: { radius, height }, infoAnchor: { x, y, z } }
```

Only an imported body has a `facingNode`. Root/body metadata is presentation
state, never serialized GameState. Side-colored miniature base geometry is a
root sibling of the imported body and never rotates with facing. Cardboard keeps
its existing printed Side faces and turnover behavior.

Resolution order is Flat token, requested Cardboard, Lying cardboard, approved
compatible ready miniature, then cardboard fallback. A Type descriptor's approved
Cell span must match the observed footprint. Unsupported, invalid, failed and
loading models never produce an empty Unit.

The router notifies the viewport at event start and authoritative checkpoints.
Preference requests and model-ready entries wait for a safe checkpoint, including
completion of transient movement and cardboard turnover. Changing representation
does not reset camera/projection, restart gameplay, submit a command, discard
history or create another context. Existing roots survive body replacement.

### GLB preparation, hosting and validation

Three remains pinned to `0.180.0`. `tools/prepare-three.cjs` additionally prepares
`examples/jsm/loaders/GLTFLoader.js` and `examples/jsm/utils/BufferGeometryUtils.js`.
Their exact bare `three` imports become `../../../build/three.module.js` so browser
and Node use the same module identity. The complete prepared graph is checked;
the provenance manifest records upstream and prepared hashes and the import
transformation. There is no CDN, import map, bundler or new npm dependency.

```text
node tools/prepare-three.cjs
node tools/prepare-three.cjs --check
```

`PlaytestHost` explicitly maps `.glb` to `model/gltf-binary` in the existing
static-file middleware. Unknown extensions remain disabled. The model loader
fetches same-origin GLB files; addon import is deferred until an approved observed
model is needed. No Draco, Meshopt, KTX2 or other decoder is installed.

`three-models.js` validates approval and local URL/version/budgets before fetching.
GLB preflight requires version 2.0, one default scene, embedded PNG/JPEG resources,
static triangle primitives, and no animations, skins, morph targets, cameras or
extensions. V1 deliberately accepts core glTF materials only. Runtime validation
checks nested/world transforms, positive nondegenerate transforms, finite vertex
attributes/indices, normals, UVs for textured materials, geometry/primitive limits
and decoded texture dimensions. Failed image decoding is a model failure even
when GLTFLoader would otherwise tolerate a null map.

Normalization measures transformed vertices once per template, centers X/Z,
grounds minimum Y to zero and applies one uniform scale to the authored target
height. The resulting radial extent must fit the descriptor; overwide assets
fail rather than receiving nonuniform stretching or
automatic horizontal shrinking. Matrix-authored child transforms are retained.
The normalized body stands at Y=.06 on the procedural base, relative to the
existing Unit root height. Bounds and label anchors are cached.

Semantic requirements such as recognizable front, absence of an authored base,
style and provenance require human asset review; geometry validation cannot
establish those facts.

### Production Zombie asset specification and registration

Preserve the untouched source at
`assets/source/models/units/zombie/original.glb`. Optional editable master:
`assets/source/models/units/zombie/zombie.blend`. Store reference views, production
notes, license/attribution information, generation tool/version and source/runtime
hashes under that same source directory. Keep source files outside wwwroot.

The approved runtime destination is
`ProjectDelve.Web/wwwroot/art/models/units/zombie/v1/zombie.glb`.

| Property | Zombie V1 contract |
| --- | --- |
| Format | Self-contained GLB / glTF 2.0, one default scene |
| Orientation | Right-handed, +Y up, +Z forward |
| Origin/pivot | Ground-centered beneath soles, vertical yaw through origin |
| Footprint | Approved 1×1; no gameplay height/facing |
| Normalized height | Initial Zombie 1.20 Cell; 1.35 recommended maximum for this asset, not a universal Type height cap |
| Horizontal radius | Catalog .38 Cell; inspected H3.1 radius .325632 after normalization |
| Base | Generated by Three: radius .46 Cell, thickness .06 Cell |
| Geometry | Target 6,000–8,000 triangles; hard maximum 12,000 |
| Draw primitives | At most two; prefer one material/primitive |
| Texture | Prefer 1024² base color, optional 512² normal and packed ORM |
| Texture limit | All 31 production miniatures use maximum 1024; preparation records and descriptor budgets agree |
| Texture memory | Prefer about 8 MiB or less including mipmaps |
| Material | Rough painted PBR, opaque where possible; no emissive lighting |
| Pose | Compact neutral shamble; readable head/hands and front/back |
| Excluded | Base, scenery, floor, lights, cameras, animation, skinning, morphs |
| Transfer size | Prefer ≤3 MiB; larger files need production review |

The runtime rejects hard descriptor/geometry/texture incompatibilities and reports
them in diagnostics. Above 8,000 triangles is advisory. Low-poly test geometry is
valid; the target range is not a minimum rejection limit. Facing direction,
physical base omission and portrait consistency remain approval criteria.

Use the approved `assets/source/units/zombie.png` portrait for gray-green skin,
thinning hair, crooked jaw and ragged brown/desaturated-green clothing. A reviewed
front/side/back full-body reference is useful before model generation, but neither
reference generation nor production model creation is implemented here. Optional
Blender processing should apply transforms, repair/simplify geometry, consolidate
materials, bake maps and export core static GLB with repeatable recorded settings.
Keep the existing Pillow portrait pipeline separate.

To add or replace an approved production model:

1. Review style, front/back, pose, rights/provenance and the full source/master.
2. Prepare the runtime export to this contract; run the Khronos glTF validator
   and review warnings, then verify runtime validation and normalized dimensions.
3. Put the approved export at the versioned runtime path above. Do not overwrite
   the source or use the test figure as the export.
4. Add an approved asset to `art/models/unit-visuals.json`'s `models` array and
   reference its ID from the existing `zombie-type` entry's optional `model`:

   ```js
   { "id": "zombie/v1", "approved": true,
     "url": "./art/models/units/zombie/v1/zombie.glb", "version": "1",
     "maxTriangles": 12000, "maxPrimitives": 2, "maxTextureDimension": 1024 }
   ```

5. Run model/viewport tests, HTTP/publish checks and real-browser acceptance with
   the approved asset. Confirm diagnostics `ready`, PBR/Off appearance, grounding,
   overhead/angled silhouette, clipping, label readability and resource budgets.
6. Version both runtime URL and descriptor when replacing an approved model.

No gameplay, scenario schema or new rendering architecture is needed. Each cached
URL/version has one shared source template and resource contract; each Type uses
its own uniform normalization. Additional static Types use the same catalog boundary. Animated assets remain
explicitly unsupported in V1.

### Complete Unit visual dimensions and shared sources

`wwwroot/art/models/unit-visuals.json` is the authoritative presentation catalog.
Schema version 1 contains `units` and `models` arrays. Every canonical Type has
exactly one `{typeId,targetHeight,maximumRadius,baseRadius}` entry; optional `model` references
an approved asset's `id`. Asset entries contain `id`, `approved:true`, local `url`,
`version`, and optional `maxTriangles`, `maxPrimitives`, `maxTextureDimension`.
Footprints, HP, movement and other gameplay content are not duplicated in JSON.

All **32** current canonical Types are included, including royal/goblin pieces,
Diamond Kart, Locomotive, Chief Mechanic, Worker and Red Dragon. Terrain Machines
are not Unit Types. Current compressed heights range from .48 (Bomb Imp) through
.55 (Goblin), .75 (Zombie), 1.00 (Troll), 1.20 (Tower Troll), to 1.45 (2×2 Red
Dragon). Vehicles start lower (.53 Kart/.58 Locomotive). These presentation defaults are
provisional and may be visually tuned without changing gameplay.

`unit-visuals.js` validates exact coverage against the existing public canonical
content endpoint `/api/designer/catalog` when Three first activates. Missing,
unknown, duplicate or invalid entries, unknown model references and unsupported
schema versions fail validation visibly rather than silently inventing dimensions.
Custom scenario Types without approved assets continue using Cardboard; they do
not expand the canonical registry. Canonical model resolution also checks the
observed footprint against the canonical footprint, so a scenario override cannot
silently approve an incompatible body.

`UnitVisualCatalogTests` compares JSON IDs with actual `CanonicalUnitTypes.All`,
`UnitTypeIds` constants and the `UnitRoster` factory inventory. This catches an
omitted registry factory as well as a missing catalog entry. Node tests exercise
negative schemas/dimensions/references and adding a new canonical ID.

To add a canonical Type: add its gameplay definition/registry entry, add one
explicit visual row with provisional positive finite height/model radius/base radius, then run
`UnitVisualCatalogTests` and `unit-visuals.test.cjs`. Attach an approved asset later
by referencing `models[].id`. No name-derived runtime dimensions hide omissions.

Height means exact intended model height above its feet, excluding the procedural
.06-Cell base. Radius is a maximum of transformed vertices about the ground-center
after uniform scaling. Radius failure keeps Cardboard; the loader never shrinks to
fit or stretches axes separately. Radial budgets are bounded to .92 Cell for
1�1 and 1.25 Cells for 2�2 footprints. Small envelopes retain nine FoW copies;
envelopes at least one Cell in radius use sixteen (at most four columns per axis). A miniature may
overhang its logical footprint and generated base without changing legality.
Clipping uses the maximum of the measured model radius and the base radius.
Picking and label width retain the independent .46 × Cell span proxy envelope. There is no
universal 1×1 height cap. See the approved Troll measurements below.

The cache keys **source** resources by approved URL/version and measures their
transformed bounds once. Height, radius and Cell span are per-Type contracts, not
source cache keys. Instance wrappers clone the source hierarchy and apply a private
uniform scale/ground offset; geometry, textures and immutable lit/Off material
pairs remain shared. Two Types can reference the same asset at 1.20/.38 and
2.40/.76, respectively, with one download/parse. A Type-specific radius failure
does not poison a ready source needed by another Type. Source triangle/primitive/
texture budgets must agree for a shared URL/version.

Camera fitting uses the public maximum registered height plus base/label allowance
(minimum 1.5), plus the greatest registered radial overhang beyond half its logical
span, independent of hidden Units and async arrivals. Coarse picking stays
on observed logical footprints and vertical proxies. Miniature labels use their
information anchor and also clear the projected proxy's top edge, preventing name
plates covering heads at overhead. LOD and width continue using the independent proxy.

### Configurable physical miniature bases V1

`baseRadius` is the authored circular base radius in Cells. It is independent of
`targetHeight` (figure height above its feet), `maximumRadius` (the permitted
overall model extent, including equipment), and the authoritative logical
footprint. It never scales or approves a GLB and never changes picking, movement,
LOS or observation. Unsupported Types still use Cardboard until they have an
approved model; their explicit base values are provisional presentation metadata.

Physical bases are capped at `.90 * min(footprintWidth, footprintDepth) / 2`.
Canonical metadata is validated against canonical footprint dimensions; runtime
layout additionally caps against the actual observed occupied-Cell dimensions.
Thus a 1×1 circle has maximum radius .45, a 2×2 circle .90, and a 2×1 circle .45.
The base and imported figure remain centered on the footprint. The sizing helper
accepts width and depth independently; only circular geometry is implemented.
No rectangular/oval/custom descriptor schema has been introduced.

Bases keep .06-Cell thickness, a neutral charcoal body/top, and a .025-Cell-wide
Side-colored top rim. Shared cylinder/cap geometry is scaled; rim geometry is
cached per radius to retain constant visible width. All of these geometries are
owned and disposed through the existing viewport registry. Yaw rotates only the
figure, so the base never moves, rotates or resizes as facing changes. Feet remain
grounded at the same base-top height. Selection, active marks and legal indicators
retain their existing logical geometry.

The contact approximation samples transformed scene vertices in the lowest 7.5%
of source height once per cached source. It records X/Z minima/maxima and maximum
radial distance **about the normalized model's facing pivot**, including any
offset of the foot area. Uniform normalization scales that envelope per Type.
Elevated weapons/arms/wings are excluded by height, without mesh segmentation.
The default target margin is .04 Cells: `requiredRadius = contact.radius + .04`.
The configured radius stays fixed; the estimate never automatically enlarges a
base or changes the imported figure. Diagnostics expose the estimate, selected
and maximum radius, actual margin, shortfall and warning for observed miniatures.

**Contact fit is advisory.** Insufficient margin or an envelope wider than the
base is a non-blocking visual diagnostic, including when the required radius
exceeds the footprint cap. It does not revoke model approval or substitute
Cardboard. Loading, genuine model validation/rendering/safety failures and Lying
Posture retain the established Cardboard fallback. Low cloth, toes, weapons,
decorations or sparse vertices can make the estimate ambiguous; it is not foot
detection or a physical support guarantee. Inspect actual imagery before tuning.

At the current compressed heights, production measurements are:

| Model | Height | Base radius | Contact radius | Contact X / Z bounds | Band / vertices | Actual margin | Required radius | Margin shortfall |
| --- | ---: | ---: | ---: | --- | --- | ---: | ---: | ---: |
| Zombie | .75 | .25 | .203520 | [-.161622,.185692] / [-.155299,.148383] | .05625 / 421 | .046480 | .243520 | 0 |
| Troll | 1.00 | .44 | .450029 | [-.416705,.437671] / [-.275222,.242219] | .075 / 561 | -.010029 | .490029 | .050029 |

Zombie has a visible margin and is much less dominated by its base. Troll retains
the approved GLB on its .44 base despite the warning, as explicitly requested:
the conservative envelope overhangs by .010029 Cells, and the .04 margin cannot
fit within the .45 cap. No deformation, height change, source modification or
footprint expansion resolves that warning implicitly. Historical model/performance
records later in this document predate the compressed heights and this base pass.

All 32 provisional circular radii (Cells):

| Unit Type | Radius | Unit Type | Radius |
| --- | ---: | --- | ---: |
| Royal Queen | .32 | Labyrinth Shaper | .33 |
| Witch Queen | .31 | Red Dragon (2×2) | .72 |
| Royal King | .32 | Grunt | .34 |
| Goblin King | .30 | Zombie | .25 |
| Royal Bishop | .31 | Ghost | .30 |
| Goblin Bishop | .28 | Skeleton Archer | .29 |
| Royal Knight | .36 | Goblin | .27 |
| Hobby Knight | .31 | Bomb Imp | .24 |
| Royal Peasant | .30 | Troll | .44 |
| Goblin Peasant | .27 | Worker | .28 |
| Castle Guard | .34 | Geomancer | .31 |
| Tower Troll | .43 | Shaman | .29 |
| Diamond Kart | .37 | Barbarian | .34 |
| Chief Mechanic | .31 | Rogue | .30 |
| Locomotive | .39 | Cleric | .32 |
| Displacer Demon | .38 | Wizard | .32 |

For a new approved model, keep height/model radius separate, measure using
`inspect(file, targetHeight)` from `tools/inspect-glb.cjs`, then choose the smallest
comfortable `baseRadius` that provides about .04 Cells of radial foot margin and
fits the footprint cap. Inspect several figure facings, overhead and angled
lighting On/Off views. Where contact geometry is ambiguous, retain the reviewed
metadata and document the diagnostic rather than changing model approval. Run
`miniature-bases.test.cjs`, model/catalog/viewport tests and `UnitVisualCatalogTests`.

Transient FoW clips the maximum of the actual model radius and selected base
radius, so smaller bases cannot reveal or hide additional model fragments.
Camera fitting still uses the public model-height/radial envelope, never the base
radius or contact diagnostic. Full/Fixed/Auto/Follow, both projections, the model
cache and invalidation-driven idle behavior are unchanged.

#### Configurable base verification — 2026-10-27

Focused model/catalog/base/viewport tests: **78 passed**. Complete browser-client
and dispatcher-security suite: **559 passed**. Warning-free cached solution build
and the focused .NET `UnitVisualCatalogTests` passed; no full .NET regression run
was needed for these client changes. Earlier production catalog tests still
expected pre-compression heights (Zombie 1.20/Troll 1.90); those two registration
expectations now use the existing .75/1.00 metadata. Historical GLB reproduction
and overhang stress tests retain their explicit taller test contracts.

`node tools/miniature-base-qa-plans.cjs` reproduces data-only plans under
`.playwright-cli/miniature-base-qa/`, reusing the existing test-owned scenarios.
Production-mode isolated 5081, restricted headed Edge, no model overrides:

| Capture directory under `.playwright-cli/browser-qa/` | Inspected evidence |
| --- | --- |
| `inspection-ixrc8P` | Zombie/Troll together, Lying Cardboard, lit/Off perspective 65°, overhead/orthographic, narrow |
| `inspection-Okr2G2` | Three adjacent Upright Trolls and Zombie; lit/Off, overhead, 600×850 layout; `bases.yaml` confirms .25/.44 and advisory Troll shortfall |
| `inspection-D7WiU7` | Final repeat of adjacent views after compact diagnostics grouped by observed Type/footprint |
| `inspection-wgJcnJ` | Settled 0/90/180/270° camera views |
| `inspection-v1isUz` | Canonical Move, positive-Z facing and grounded figure |
| `inspection-ChRHYt` | Attack-facing, selection/active emphasis and combat on test-owned stats |
| `inspection-tSictM` | Canvas-center picking submits legal Stay; Smash Door spatial-facing and action feedback |
| `inspection-Qwn9O4` | Initial Lying Cardboard and restored Upright Troll |
| `inspection-wwQQ05` | Patrol hidden/entered/left FoW and fresh restart exploration |
| `inspection-HOHHDp` | Repeated Cardboard/Miniatures and Flat/Classic/Enhanced/Tabletop cycles, restart/reload; one GLB fetch, shared source geometry/textures |
| `inspection-L39Pt7` | Reload then ordinary Tabletop selection restores the production Troll |

Captured PNGs were opened and visually inspected. Zombie's feet have a visible
neutral margin; Side rims remain readable. Adjacent Troll bases stay separate,
with no geometry overlap; the figure may overhang its own base as reported by the
diagnostic. The compressed figures and fixed-thickness bases remain grounded.
Hidden patrol bases disappear with their Units and return together. Projection,
facing, information, picking and renderer-switch behavior showed no regression.
Console checks were empty and no asset requests failed. Refresh/restart during
movement/combat plans aborted superseded `/api/game/updates` SSE requests normally.

Remaining manual gaps: frame-exact transient FoW pixel assertions, native touch,
real-driver context loss and sustained GPU/low-end performance. Existing CPU
tests cover clipping geometry, failure fallback, context recovery and cleanup;
these are not hardware measurements. Low-vertex contact is approximate and the
Troll margin shortfall intentionally remains advisory. Types without approved
GLBs have provisional radii awaiting visual review with their eventual models.
No Engine/server-observation implementation, gameplay, scenarios, GLBs or
`CONSTRAINTS.md` changed. The original 5080 host was preserved.

### Facing, Posture, FoW, picking and camera

`unit-facing.js` stores decoration separately from bodies. Initial heading hashes
`visualSeed` and observed Unit ID under versioned domains: eight sectors plus a
stable ±6° offset. Session replacement clears records but session ID is not a
hash input, so restart reproduces initial headings. Rerenders, loading and body
switches retain the current record. Browser reload begins from the displayed
baseline; historical orientation is not persisted in storage or GameState.

Observed nonzero movement legs use `atan2(dx,dz)` plus the stable offset. A leg
needs an observable supplied endpoint/footprint; hidden-only padding, singleton
segments, zero displacement and disconnected gaps supply no heading. Boundary
entry/exit legs use only supplied geometry. No hidden route is reconstructed.

At AttackStarted, the actor turns toward an observed selected Cell, otherwise
the first resolvable observed target footprint center in supplied order. Action
use and Door events may resolve their supplied Cell/Unit/Machine/Edge target.
Missing spatial information retains heading. Defenders do not automatically turn.
The app observes facing outside animation gates and resolves attacks against the
matching displayed after-frame, including newly observed endpoints, never the
latest response's future positions. This runs even with Three inactive.

Yaw uses the existing RAF, about 150ms cubic easing and
`atan2(sin(target-current),cos(target-current))`. Retargeting starts at the
displayed angle. Skip/reduced motion settle to the same final observed heading
without adding pauses. Cardboard, including fallback, never receives yaw.

Initial Lying and miniature-to-Lying use settled reverse-face cardboard. Standing
restores a ready miniature with stored facing, or cardboard while loading.
Cardboard-to-cardboard retains existing turnover. Unit root, footprint and
information remain authoritative throughout.

Stable hidden Units are absent from observed figure input and never instantiated.
Stable observable multi-Cell bodies remain whole and unclipped. Transient movement
uses the displayed explored set and nine or sixteen reusable Cell-column copies. Hierarchy
copies preserve nested matrices and material arrays; every mesh gets private
clipping variants of both PBR and Off materials. Planes remain world-aligned while
all facing pivots share yaw. The validated radial envelope bounds intersecting
columns to nine for either footprint (compact 1×1 models still need at most four).
Height does not weaken vertical planes.
Transient labels remain absent. Cancellation releases only its own pool/source,
so a stale movement cannot touch a replacement session or newer movement.

Picking still maps ground Cells, footprints and Door proxies to supplied opaque
candidates. Imported triangles are never raycast for legality. An observed
miniature additionally has a coarse vertical box; nearest ray distance wins with
a stable ID tie-break. A head above the ground horizon can still hit this proxy.
Door and supplied Position-choice priority are preserved, and proxy hits map back
to complete logical footprints. Board choices remain the accessible alternative.

Tabletop Full/Auto/follow and near-plane safety use a public catalog-derived visual
envelope: at least 1.5 Cells, or the tallest registered Type's target height plus
.15 Cell. It is independent of observation, loading and representation preference.
Model arrival therefore does not change framing. The model
anchor positions HTML information above the figure; base projection primarily
determines LOD and width. Neither envelope nor model height is an engine stat.
Taller Types have explicit catalog heights; no Engine Height is introduced.

### Lighting, cache ownership and diagnostics

The existing directional key/ambient rig illuminates imported Standard materials.
The cache registers one shared Basic/Standard pair per imported material. Off
preserves base color, texture, alpha and raster settings; existing portraits stay
unlit. Loading while Off immediately resolves the correct variant. There are no
new lights, shadow maps, environment maps, glow or tone mapping.

The viewport owns one promise/state entry per approved URL/version: loading,
ready or failed. Loading never blocks a decision. Independent hierarchy clones
share geometry, textures and material pairs. Failures remain cached without retry
storms. Completion only marks current bodies eligible at a safe checkpoint; it
never attaches captured roots from an old session, removed Unit, prior Posture or
inactive viewport. Context loss retains CPU assets for existing recovery/fallback.

Per-Unit removal detaches instances; it does not dispose shared assets. Clipping
pools dispose only their material clones. Viewport disposal aborts fetches,
disposes unique geometries/material variants/textures, closes owned image bitmaps
and releases cache references. Late parser completion is disposed without attach.
Static miniatures add no continuous RAF or skeletal work.

Diagnostics include preference, visible model roots, cache states/errors,
fetch/decode/total milliseconds, resource timing fetch count, model triangle and
primitive counts, resource counts and estimated texture bytes. Decode includes
lazy addon import and normalization; GPU upload/completion is not measured.

### Test fixtures and verification

`ProjectDelve.Web.Tests/fixtures/miniatures/static-figure.glb` is a deterministic
84-triangle, two-material, 9,916-byte procedural test figure. Its orange front,
vertical silhouette and nested transforms exercise the import path. It is not
production artwork.

The companion `textured-figure.glb` (10,404 bytes) embeds a deterministic 8×8 PNG
checker. It exercises real browser image decoding, sRGB maps and shared PBR/Off
materials without changing the untextured performance fixture.

Reproduce/check both with:

```text
node tools/prepare-miniature-fixture.cjs
node tools/prepare-miniature-fixture.cjs --check
node --test ProjectDelve.Web.Tests/three-models.test.cjs ProjectDelve.Web.Tests/unit-facing.test.cjs ProjectDelve.Web.Tests/three-viewport.test.cjs ProjectDelve.Web.Tests/door-choice.test.cjs ProjectDelve.Web.Tests/three-camera-geometry.test.cjs
```

HTTP tests cover explicit MIME, actual bytes and rejection of unknown extensions.
QA registration requires BOTH Development and `--MiniatureQa true`. Only then
does `/__qa/miniatures/` expose the test directory and its explicit registration
module; Production ignores the flag. Production registration uses the normal JSON catalog. Source/test
assets are not ordinarily published. `/api/presentation/miniatures-qa` reports only
whether this explicit test mode is enabled, never gameplay/observation data.

After the cached Debug build, `node tools/test-miniature-publish.cjs` runs actual
Web SDK publish with an explicitly supplied test-only Content target. It verifies
GLB inclusion, local loader/module inclusion, production Zombie inclusion, source exclusion and absence of QA registration. The production project never imports
that target.

Start the isolated host using BROWSER_INSPECTION.md, adding
`--environment Development --MiniatureQa true`. `node tools/miniature-qa-plans.cjs`
prepares test scenarios and data-only dispatcher plans under
`.playwright-cli/miniature-qa/`. Validate and inspect them through the existing
protected runner. `combat-settled.json` intentionally observes the completed game
after `combat.json`; it does not replace it. Performance and other fixture plans
import their own scenarios. Preserve the original 5080 listener.

Focused tests cover static validation/failure, actual GLB parsing, transformed
bounds, shared resources, late disposal, image decode failure/bitmap cleanup,
preference/checkpoint fallback, root identity, yaw and Posture, skipped/inactive
playback, event-local attack positions, hidden movement, imported multi-Cell
figures, matrix/material-array clipping, proxy priority/horizon, lighting,
replacement, context recovery and idle rendering. Real scenarios are not fixtures.

### Edge measurements and evidence — 2026-10-27

Restricted headed Edge, isolated 5081, Full Board, perspective FOV 65, fresh contexts.
One additional unsupported cardboard Unit is present. The figure has **84
triangles and no textures**, so these measure infrastructure overhead, not an
8,000-triangle production Zombie. Warm timing snapshots contain only 6–7 camera
frames; these are short functional samples, not GPU benchmarks or FPS guarantees.

| Board | Models | Fetch ms | Decode/normalize/import ms | Warm CPU p95 ms | Camera interval p95 ms | Calls | Total triangles |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 30×20 | 1 | 9.0 | 13.7 | 1.3 | 16.7 | 17 | 1,598 |
| 30×20 | 10 | 10.6 | 14.0 | 1.9 | 16.7 | 62 | 3,434 |
| 30×20 | 30 | 11.2 | 17.6 | 2.2 | 16.7 | 162 | 7,514 |
| 30×20 | 100 | 21.5 | 19.3 | 4.2 | 16.7 | 512 | 21,794 |
| 50×50 | 1 | 11.7 | 190.0 | 1.6 | 16.7 | 17 | 5,398 |
| 50×50 | 10 | 10.8 | 20.5 | 1.9 | 16.7 | 62 | 7,234 |
| 50×50 | 30 | 12.9 | 18.9 | 2.4 | 16.7 | 162 | 11,314 |
| 50×50 | 100 | 26.3 | 20.0 | 4.0 | 16.7 | 512 | 25,594 |

Each model cache retained two geometries, four material variants and zero textures,
independent of count. The sampled scenes retained five to six uploaded textures, eight
geometries and 31 cached materials. Cold import variability is visible in the table;
it must not be compared with warm submission timings. A textured production model
will need separate texture/upload/GPU measurements. At 8,000 triangles, 100 models
would add about 0.8 million model triangles before clipping copies; no such GPU
performance claim is established by this fixture.

The observed FoW patrol recorded 26 movement frames: movement CPU p95 2.1ms,
interval p95 16.8ms; settled scene 27 calls/750 triangles. Five Cardboard/Miniatures
pairs plus Flat/Classic/Enhanced/Tabletop switching retained one ready model entry,
two model geometries/four variants and one resource-timing GLB fetch. First use of
Flat uploads additional existing geometry/shader variants; cache growth at that
boundary is expected. Missing/invalid fixture diagnostics each retained one failed
entry and one fetch through repeated preference changes.

Evidence under `.playwright-cli/browser-qa/`:

- `inspection-YEAyFP`: mixed scene, Side bases, Off/On, angled/overhead/narrow.
- `inspection-SE0Yle`, `inspection-OEL1TK`, `inspection-jSkLkH`, `inspection-wKndDi`: final 30×20 counts 1/10/30/100.
- `inspection-MYQ99l`, `inspection-KxAef6`, `inspection-GnFXLD`, `inspection-YiemSG`: final 50×50 counts 1/10/30/100.
- `inspection-06KG1M`: imported patrol hidden/entered/left/restart and movement timing.
- `inspection-SEN48i`: repeated preference/renderer switching, cache/fetch reuse, restart/reload.
- `inspection-RHSwUA`: initial Lying and Upright restoration.
- `inspection-uDEpMu`: intentional missing/invalid fixture failures and playable fallback.
- `inspection-xeRNSV`, `inspection-DMO5so`: supplied Attack choice/dice, orientation and settled Lying result.
- `inspection-ZZbYEv`: generated dungeon through Play Unseen; no canonical model falsely activated.
- `inspection-B6Cnu9`: real embedded PNG decoding and visible checker in Lighting Off/On; one shared model texture and one GLB fetch.

Earlier fixture-baseline complete client/security suite: **540 passed**. Cached solution build:
**zero warnings/errors**. Complete .NET solution tests: **1,982 passed**
(1,601 Engine, 381 Web), including four new MIME/QA-isolation HTTP checks.
Vendor/fixture integrity and actual publish checks also passed. No Engine,
server-side observation, scenario schema, `CONSTRAINTS.md` or protected runner
implementation was changed.

Normal successful runs reported no console errors or missing assets. The deliberate
missing-GLB run reports its expected 404; refresh/restart runs can abort superseded
SSE requests. Earlier draft plans that toggled an already-open disclosure or waited
for an automatically completed End Turn were corrected; a proposed complex selector
was rejected by the runner and removed. No protected runner or permission changed.

Remaining manual gaps: native canvas/touch selection, frame-exact transient FoW
pixel assertions, real-driver context loss, injected network timing, long-duration
resource/GPU/low-end performance and final painted production appearance. Node
tests cover input/clip/lifecycle math but do not establish those hardware results.
The following production acceptance section supersedes the fixture-only results.

### Production H3.1 Zombie — 2026-10-27

Historical integration snapshot. Current runtime sizes, hashes and texture budgets
are recorded in [Runtime texture standardization to 1024](#runtime-texture-standardization-to-1024--2026-10-28).

The owner-supplied replacement is Tripo **H3.1 generated under Pro**, confirmed by
the owner. Reviewed [paid-user terms](https://www.tripo3d.ai/terms) section 5.2.2
include publication, distribution and licensing. Together with Pro commercial
use, this supports game/web hosting and source/runtime repository inclusion;
no standalone-output exclusion was found. Agreement/third-party-rights conditions
remain applicable. Provenance records the basis and unknown generation/export
details; it does not invent a new public asset license.

Source: `assets/source/models/units/zombie/original.glb`, **4,466,380 bytes**, SHA-256
`9f3804e8fc44deccf126efdec1f8babe2564adff45b4c53fbd131ef1a3823eb3`.
The original remains byte-for-byte unchanged. Runtime:
`ProjectDelve.Web/wwwroot/art/models/units/zombie/v1/zombie.glb`, **4,466,452 bytes**,
SHA-256 `c3f15a9e0be475aebf336ca0ed5704f5dbfc2b323e8446711f642e352b6bea79`.
Both files are eligible for version control; source stays outside published content.

Inspection found glTF 2.0, one scene/node with identity transforms, one mesh,
primitive and material, **9,674 triangles**, and three embedded 2048×2048 maps
(base color JPEG, metallic-roughness JPEG and normal PNG). Positions, normals and
UVs are finite. No external resources, animations, skins, cameras, lights or
miniature base. Raw bounds are .485535 × .974670 × .403641 with minimum Y zero.
Uniform scale 1.231185 yields height 1.20 and radius .325632. Catalog height stays
1.20 and the radius budget is .38. Browser observed +Z movement confirms the front
axis; no axis correction is necessary. Feet stand on the procedural .06-Cell base.

`tools/prepare-unit-model.cjs` is a reusable, provenance-driven exporter. It verifies
source hash and reviewed rights, rejects actual extension payloads, removes unused
extension declarations, adds source-hash metadata, and preserves all binary mesh
and image bytes. Runtime does uniform normalization/ground-centering. There is no
decimation or texture resizing: short measurements did not justify quality loss.

```text
node tools/inspect-glb.cjs assets/source/models/units/zombie/original.glb
node tools/prepare-unit-model.cjs assets/source/models/units/zombie/provenance.json
node tools/prepare-unit-model.cjs assets/source/models/units/zombie/provenance.json --check
```

The catalog's `zombie/v1` asset uses version `h3.1-v1`, 12,000-triangle/two-primitive
limits and an explicit 2048 texture budget. Only `zombie-type` references it.
On an ordinary host select Tabletop → Miniatures. Upright Zombies load asynchronously;
Lying, unsupported, loading and failed bodies use Cardboard. Flat remains unchanged.
There is no special Zombie route, environment restriction, flag, browser registration
request, binary ignore rule, publish blacklist or alternate runtime copy. Obsolete
preview code/tool/tests and its prepared model were removed. General fixture QA
isolation, source integrity, MIME and resource ownership remain in place.

For the next model: create a reviewed source/provenance directory with original
and runtime paths/hashes, run the same exporter with that provenance filename,
validate geometry/maps/normalized proportions, add an approved catalog asset and
Type reference, and run model/catalog/HTTP/publish/browser checks. Multiple Types
may share the same URL/version at different scales without another download/parse.
No gameplay or rendering architecture migration is required.

Restricted Edge used the **Release** binary with ordinary Production defaults on
isolated 5081, without model flags. Performance scenes contain one unsupported
Cardboard Unit and Full Board perspective FOV 65. These are fresh-browser local
loads and short 6–8-camera-frame samples, not GPU completion or sustained FPS.

| Board | Zombies | Fetch ms | Decode/import/analysis ms | Render CPU p95 ms | Interval p95 ms | Calls | Total triangles |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 30×20 | 1 | 35.0 | 92.2 | 1.9 | 16.7 | 16 | 11,188 |
| 30×20 | 10 | 38.9 | 80.8 | 1.9 | 16.7 | 52 | 99,334 |
| 30×20 | 30 | 38.6 | 99.1 | 2.7 | 16.7 | 132 | 295,214 |
| 30×20 | 100 | 221.3 | 94.0 | 4.7 | 16.7 | 412 | 980,794 |
| 50×50 | 100 | 54.1 | 94.9 | 4.5 | 16.7 | 412 | 984,594 |

All counts retained one GLB fetch, one source geometry, two shared material variants
and three textures. Estimated RGBA+mipmap texture memory is **64 MiB**, independent
of instance count, not a measured GPU allocation. Renderer inventory was seven
geometries, nine uploaded textures and 31 cached materials. The 221ms fetch is an
observed local outlier, not discarded. The warmed FoW patrol recorded 26 movement
frames: CPU p95 2.1ms, interval p95 16.8ms; camera CPU p95 2.6ms. Repeated preference/
renderer switching retained one fetch and shared resources. Static models remain idle.

Inspected evidence under `.playwright-cli/browser-qa/`:

- `inspection-Y1k9qF`: mixed Cardboard/miniatures, physical base/grounding,
  Lighting Off/On, overhead, Orthographic, 65° perspective and narrow layout.
- `inspection-gJ8m1Q`: canonical Zombie legal Move and retained +Z front direction.
- `inspection-LarljH`: observed Attack choice/roll and settled target orientation.
- `inspection-QvhtaS`: initial Lying Cardboard and restored Upright model.
- `inspection-sGTbPK`: hidden enemy, observed entry/exit, restart and movement samples.
- `inspection-xklgoH`: five preference pairs, Flat/Classic/Enhanced/Tabletop switches,
  restart/reload, stable model/cache counts and one fetch.
- `inspection-xWu5Gm`: generated dungeon through Play Unseen; visible party stayed
  Cardboard and concealed monsters did not trigger model instances.
- `inspection-1KZ5lY`, `inspection-OC3lam`, `inspection-yBsd7Q`, `inspection-0qs20O`:
  30×20 counts 1/10/30/100; `inspection-LatWL7`: 50×50 stress sample.

Images show aligned bases, grounded feet, readable front/back, overhead labels and
clear mixed fallback. Lighting is darker at default fill than Off; adjusted fill
helps. Fine detail remains small at narrow full-board zoom. Console reports were
empty; no asset request failed. Restart/refresh aborted superseded SSE streams only.

Verification: **547 client/security tests**, **1,985 .NET tests** (1,601 Engine,
384 Web); Debug/Release solution builds with zero warnings/errors. Seven focused
catalog/hosting cases pass in Release as well. Vendor integrity and reproducible
export checks pass. Debug and Release ordinary publish outputs each contain exactly
one production GLB and no source/test copies; the separate test-target publish still
verifies fixture inclusion and exclusion of its registration module.

CPU tests cover the actual model's grounding, scale, coarse picking and transient
clipping in both lighting modes. Their in-memory geometry copy omits map bindings
because Node has no image decoder; Edge loads the real embedded textures. Existing
tests retain hidden/disconnected/padded observation, shortest-yaw/Skip/reduced-motion,
root identity, async cancellation, cleanup, legal proxy priority and context recovery.

Manual gaps remain for native canvas/touch input, frame-exact transient/disconnected
FoW pixels, injected network delays/context loss in a real driver, browser reduced
motion emulation, and sustained GPU/low-end/resource testing. No GPU performance
claim or independent exclusivity/copyright guarantee is established. There is no
remaining technical migration blocker; generation/export records remain unknown.

Migration files: source/runtime GLBs, catalog, provenance/asset README,
`tools/prepare-unit-model.cjs`, `tools/zombie-qa-plans.cjs`, publish checker,
`PlaytestHost.cs`, `app.js`, model defaults comment and catalog resolver comment,
model/viewport/hosting/catalog tests and this document. Old preview-only script,
test and prepared bytes were deleted; unrelated V1 changes were preserved.
No Engine, server observation, scenario format, rules or `CONSTRAINTS.md` changes.

### Production Troll — 2026-10-27

Historical integration snapshot. Current runtime sizes, hashes and texture budgets
are recorded in [Runtime texture standardization to 1024](#runtime-texture-standardization-to-1024--2026-10-28).

Troll is the second approved production GLB, using the same cache, loader,
preparation, material pairs, base, facing and fallback path as Zombie. There is
no Troll-specific rendering branch, preview flag, route, renderer or context.
Tabletop Miniatures uses it Upright; Lying uses Cardboard and standing restores
the ready model with retained facing. Loading/failed assets retain Cardboard.
Zombie's H3.1 source/runtime bytes and descriptor remain unchanged; other Types
remain Cardboard. Flat and DOM modes retain their existing presentation.

Source: `assets/source/models/units/troll/original.glb` (4,370,304 bytes).
Runtime: `wwwroot/art/models/units/troll/v1/troll.glb` (4,370,372 bytes), catalog
ID `troll/v1`, export version `tripo-v1`. Source README/provenance contain full
hashes, dimensions, hierarchy, maps, references and paid Pro rights review. The
exact Tripo generation model/date/settings and export receipt are unknown.
The existing preparation tool retains all geometry and three 2048-square images,
removes unused exporter extension declarations and adds source-hash metadata.
Reproduction checks pass for both Troll and Zombie. No source bytes changed.

Troll is one static 9,504-triangle mesh/primitive/material, with base-color,
metallic/roughness and normal maps, no external resources, skin, animation,
morph, lights, camera or authored base. Identity root, determinant 1, finite
geometry. Original dimensions are .867676 x .991241 x .545624 (X/Y/Z).
Uniform scale 1.916788 yields **1.663151 x 1.90 x 1.045845 Cells**. Measured
radius is **.8550545154**. Its catalog maximumRadius changes **.44 → .856**;
targetHeight stays **1.90**. No shrink, axis distortion or footprint change.
It is 58.3% taller than Zombie (1.20), with about 2.78 times its X width.
Edge's observed +Z Move verifies +Z forward; +Y up and base-top grounding are
visually consistent across views. The pose and club overhang the .46-radius
base and can overlap adjacent silhouettes; at ordinary board framing the Troll
is large and readable without obscuring the whole board.

Khronos validator 2.0.0-dev.3.10 reports zero errors for original and runtime.
Both have one generated-tangent-space warning: normal-map tangent appearance
can vary between implementations. The source has one informational unsupported
unused FB_ngon_encoding declaration, absent in runtime. All original images decode
successfully in Edge. The 8,000-triangle target is advisory, with 12,000 as the hard
limit; no optimization was justified by the local measurements below.

#### Bounded generic visual overhang

The user explicitly authorized the visual-envelope extension after inspection
showed the source could not fit the former 1x1 .46 limit at 1.90 height.
`maximumModelRadius` is .92 for both footprint sizes, retaining the previous
largest 2x2 envelope and the existing nine-copy pool. A radius below one Cell
intersects at most 3x3 world Cell columns at any yaw or sub-Cell position.
Larger descriptors are rejected; no dynamic/unbounded clipping allocation was
introduced. Unit visual metadata and model descriptors enforce the same bound.

Imported bodies expose their actual normalized radial extent (or base radius,
whichever is larger) to transient clipping. The existing pool therefore clips
the club/arms to every intersecting explored column, using the displayed explored
set and private lit/Off material variants. Hidden stable content stays absent;
whole observable stable bodies retain the existing unclipped semantics.
Transient labels stay hidden and disconnected segments remain separate.

The body separately exposes a base-sized pick radius. Protrusions do not enlarge
selection boxes, intercept neighboring legal Cells or supply new candidates.
Ground, supplied Position and Door priorities remain intact; imported triangles
are never used to determine legality. Logical footprints remain authoritative.
Label width/LOD still use the base; height anchors remain above the model.

Camera fitting uses public registered metadata: height 2.05 including allowance,
and maximum overhang .356 beyond half a 1x1 span. Full, Auto, follow and near-plane
bounds include the overhang independently of observation or async model loading.
Full/Auto fitting expands only visual bounds, without modifying gameplay Cells.
When a board-edge center clamp prevents a preferred shift, Auto fits the active
visual envelope at that final center. Flat clears these presentation allowances.

#### Restricted Edge measurements

Production-mode isolated 5081 host, headed Edge, 1440x1000. Each stress scene is
a dedicated 30x20 Stone Floor board with N canonical Trolls and one Cardboard
Grunt. Perspective 65 degrees, Full Board, lighting On. Reset timing sample after
the model is ready; eight alternating rotations exercise warm rendering. Counts
are complete scene submissions including bases/terrain/feedback, not just GLB
triangles. Short local CPU submission/frame-interval samples are not GPU timings
or sustained FPS guarantees.

| Trolls | Model triangles | Scene triangles | Calls | Fetch / decode ms | Total load ms | Warm CPU render p95 ms | Frame interval p95 ms | Camera samples |
| ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 1 | 9,504 | 11,018 | 16 | 48.4 / 90.5 | 139.1 | 1.8 | 16.8 | 28 |
| 10 | 95,040 | 97,634 | 52 | 31.9 / 78.4 | 110.4 | 1.6 | 16.7 | 14 |
| 30 | 285,120 | 290,114 | 132 | 201.5 / 74.9 | 276.6 | 2.1 | 16.8 | 16 |
| 100 | 950,400 | 963,794 | 412 | 51.4 / 98.0 | 149.6 | 3.6 | 16.7 | 11 |

All four cases have one fetch, one shared model geometry, one material with two
lit/Off variants, and three shared textures (64 MiB estimated decoded RGBA plus
mips). The complete scenes retain nine uploaded textures, seven geometries,
eight programs and 67.92 MiB estimated textures, independent of Troll count.
Like Zombie, this source exceeds preferred transfer/texture targets; Troll has
170 fewer triangles than Zombie's 9,674. No GPU or low-end-device comparison was
measured, and no destructive optimization was performed.

The test-owned two-Troll patrol with FoW recorded 27 movement frames, movement
CPU render p95 **1.7 ms**, interval p95 **16.8 ms**. The same patrol without FoW
recorded 45 movement frames, CPU p95 **1.3 ms**, interval p95 **16.8 ms**. These
local samples differ in visible terrain submissions and animation sample count;
the .4 ms difference is an observed comparison, not isolated GPU clipping cost.
Repeated Cardboard/Miniature and Flat/Classic/Enhanced/Tabletop switches retained
one fetch and the same model resources; after two complete renderer cycles,
texture/geometry/program counts settled at 9/7/8. Restart/reload retained normal
production registration and fallback behavior.

#### Evidence and automated verification

Generate data-only plans using `node tools/troll-qa-plans.cjs`; run them through
the protected dispatcher described in BROWSER_INSPECTION.md. This uses existing
test-owned scenario generators, canonical production assets and ordinary UI.
No authored scenario layout or Unit configuration is asserted by tests.

Captured and inspected PNGs under `.playwright-cli/browser-qa/`:

- `inspection-LOu8M5`: Troll beside Zombie/Cardboard, Lying fallback, lit/Off,
  perspective 65 degrees, overhead, orthographic and 600x850 narrow layout.
- `inspection-iXyesw`: settled 0/90/180/270-degree camera rotation views.
- `inspection-1Pvzxc`: canonical legal Move and positive-Z front orientation.
- `inspection-ebMtEy`: Attack-facing and combat feedback, dedicated test stats.
- `inspection-bGAQoU`: canonical Smash Door spatial-facing and action feedback.
- `inspection-5lerBu`: actual canvas-center pick submits the legal Stay choice;
  the captured action frame shows Moves 1/1, selection, active mark and Unit card.
- `inspection-7MZwNd`: initial Lying Cardboard and restored Upright Troll.
- `inspection-1aJwMu`: hidden/entered/left FoW patrol, restart/reload and timing.
- `inspection-8uNjg6`: matching revealed patrol comparison.
- `inspection-5aTagC`: representation/renderer cycles, settled resources,
  restart and reload (final capture occurs after the initial loading snapshot).
- `inspection-95JAVd`: reload followed by ordinary Tabletop selection restores
  the production Troll; initial diagnostics catch loading before the restored PNG.
- `inspection-yGi8wJ`: generated 30x20 dungeon with hidden models absent.
- `inspection-nVA0Ex`: matching revealed generated dungeon with two production Trolls.
- `inspection-LVv2Sp`, `inspection-aZBdnL`, `inspection-tH5Lt6`,
  `inspection-0RDfkb`: 1/10/30/100-Troll measurements and loaded/warm screenshots.

Successful plans had no console errors or failed asset requests. Refresh/reload
can abort the superseded SSE request. Early Door QA fixtures had an invalid
removed-Side Objective/raced an automated turn; corrected single-Unit fixtures
pass without errors. This required no engine or scenario-validation changes.

Focused model/catalog/viewport/camera tests and seven .NET hosting/catalog tests
passed, followed by **552 client/security tests**, **1,985 .NET tests** (1,601
Engine / 384 Web), warning-free solution build and actual fixture/ordinary publish
verification. Ordinary output contains exactly Zombie and Troll GLBs; source and
QA registration are excluded. Tests preserve Zombie/Flat/Cardboard contracts and
cover source hashes/reproduction, registration, oversized normalization, base
picking, all nine clipping columns, entry/exit/disconnected movement, lighting,
camera edges/aspects/yaws, Posture, shared resources and cleanup.

Remaining manual gaps: frame-exact transient FoW pixel assertions, native touch,
real-driver injected context loss/network failure and sustained GPU/low-end tests.
CPU geometry/lifecycle/failure tests cover these contracts, but do not establish
those hardware behaviors. Dense adjacent Trolls can visually overlap and users
can manually zoom far enough to crop a figure. No unresolved integration blocker;
generation details and tangent-space portability remain the genuine uncertainties.

Changed files: catalog/runtime GLB; source README/provenance and supplied source/
reference assets; `unit-visuals.js`, `three-models.js`, `three-viewport.js`,
`three-camera.js`, `three-camera-geometry.js`; model/catalog/viewport/camera/hosting
tests; `tools/troll-qa-plans.cjs`, `tools/test-miniature-publish.cjs`; this document.
The preparation tool and clipping-pool implementation are reused unchanged.
**No Engine or server observation implementation files changed.** No rules,
logical footprints, authored scenarios or `CONSTRAINTS.md` changes.

## Batch import five Tripo miniatures — 2026-10-28

Historical integration snapshot. Current runtime sizes, hashes and texture budgets
are recorded in [Runtime texture standardization to 1024](#runtime-texture-standardization-to-1024--2026-10-28).

Goblin, Barbarian, Cleric, Hobby Knight and Worker are approved ordinary production
miniatures, alongside the unchanged Zombie and Troll. All five supplied files
were compatible; none were substituted, optimized, failed or deferred. Upright
Tabletop Miniatures resolves through the existing catalog/cache. Lying, loading,
failed, unsupported and unregistered Types retain Cardboard. Other renderer modes,
all logical footprints, camera limits, clipping and picking are unchanged.

The initial inbox contained exactly those five GLBs. Additional exports arrived
later and remain unimported. Repeating this task's explicit `--only` selection
returns `[]` without touching those arrivals or rewriting completed assets.
Existing untracked portrait/reference artwork was preserved.

### Workflow and production paths

`assets/import/models/` is the standard manual inbox. Follow its
[README](assets/import/models/README.md): export a Tripo GLB, use the exact canonical
slug, place it in the inbox, run `node tools/import-unit-models.cjs`, inspect the
JSON/geometry and both Khronos reports, record reviewed hash-bound provenance,
then run with `--apply`. `--only slug,slug` restricts a task's scope. The small
wrapper reuses `inspect-glb.cjs`, runtime preflight/contact queries and the
unchanged `prepare-unit-model.cjs`; it adds no import framework or runtime branch.

The importer handles each file independently, refuses existing source/runtime/
registrations, preserves catalog numeric spelling, writes runtime/source copies
exclusively, verifies original SHA-256 before and after preservation, updates
provenance/catalog and only then removes the successful inbox copy. Incompatible,
unreviewed or failed inputs stay in the inbox. Empty/unselected runs do no work;
archived sources are never scanned. An interrupted write retains input and any
recovery evidence for inspection. No watcher, background service or UI is added.

For every slug below:

- Source: `assets/source/models/units/<slug>/original.glb`.
- Provenance/notes: the same directory's `provenance.json` and `README.md`.
- Runtime: `ProjectDelve.Web/wwwroot/art/models/units/<slug>/v1/<slug>.glb`.
- Model ID `<slug>/v1`, version `tripo-v1`, canonical Type `<slug>-type`.

`tripo-v1` identifies this repository export, not an inferred generation model.
All five use the established 12,000-triangle, two-primitive, 2048-texture budget.
Registration is in `wwwroot/art/models/unit-visuals.json` with ordinary Release
static asset packaging. Source files stay outside published wwwroot.

### Geometry, size and provenance

All five are self-contained glTF 2.0 with one identity-transform root (positive
determinant 1), mesh, triangle primitive and opaque PBR material. POSITION, NORMAL,
TEXCOORD_0; finite geometry, zero minimum Y, no skinning, animation, morph, external
resources, lights, cameras or authored base. Each retains three embedded 2048²
images: base-color JPEG, metallic/roughness JPEG and normal PNG. All fifteen images
fully decode using Pillow and in Edge. Each model estimates 64 MiB decoded RGBA
plus mipmaps. No simplification, map resizing, axis correction or deformation.
Preparation removes unused exporter extension declarations and adds source-hash
metadata, retaining all binary geometry and image bytes reproducibly.

| Slug | Source / runtime bytes | Triangles | Height | Actual / allowed radius | Contact radius | Base radius | Contact margin |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| goblin | 5,008,652 / 5,008,720 | 9,852 | .55 | .131841 / .32 | .131841 | .27 | .138159 |
| barbarian | 4,449,200 / 4,449,268 | 9,456 | .80 | .227138 / .42 | .194523 | .34 | .145477 |
| cleric | 4,159,960 / 4,160,028 | 9,854 | .78 | .194711 / .39 | .187447 | .32 | .132553 |
| hobby-knight | 4,400,204 / 4,400,276 | 9,849 | .68 | .207796 / .40 | .168170 | .31 | .141830 |
| worker | 4,343,092 / 4,343,160 | 9,756 | .63 | .189016 / .34 | .189016 | .28 | .090984 |

Dimensions are Cells after uniform target-height normalization. Full original and
normalized X/Y/Z bounds, materials, image dimensions/hashes, hierarchy and transforms
are in each provenance record. Contact is the conservative low 7.5%-height vertex
band about the model's facing pivot. Every new model exceeds the target .04 margin;
none changes `targetHeight`, `maximumRadius` or `baseRadius`. All remain 1×1.
Zombie's current height .75/base .25 and Troll's height 1.00/base .44 remain
unchanged; Troll's existing contact-margin warning remains advisory.

Khronos glTF Validator 2.0.0-dev.3.10 reports **zero errors** for all ten original/
runtime validations. Each has one generated-tangent-space warning. Each original
also has an informational unsupported unused `FB_ngon_encoding` declaration;
prepared exports have no extension declarations/payloads. Tangent-space appearance
can vary between implementations. Triangle counts exceed the advisory 8,000 target
but fit the hard budget; transfer size and decoded texture memory exceed preferred
defaults. Local measurements below support retaining supplied quality for V1.

All originals identify Tripo. The user confirms paid Pro generation for this exact
batch. Exact generation model/version, date, settings, selected generation input
and export receipt remain **unknown**. Existing portrait and character-sheet/
full-body reference paths are recorded without claiming they were generation inputs.
Full source/runtime SHA-256 and equal before/after-move hashes are stored per model.

Rights reuse the established Zombie/Troll basis, checked once for this batch on
2026-10-28: [Tripo terms](https://www.tripo3d.ai/terms), still dated 2025-07-11,
section 5.2.2, and [Pro commercial-use pricing](https://www.tripo3d.ai/pricing).
The paid-user publication, distribution and licensing rights support the existing
game/web/repository distribution policy, subject to the agreement and third-party
rights. Existing competing-service restrictions and service-provision license are
recorded in provenance. No separate public asset license or exclusivity is invented.

### Restricted Edge gameplay and visual findings

Production-mode isolated 5081 host; original 5080 listener/PID 16984 preserved.
`node tools/batch-model-qa-plans.cjs` produces data-only plans for the protected
dispatcher. All scenarios are test-owned and use ordinary production registration.
Canonical mixed/detail/posture scenes retain real stats. Move/Attack/picking
fixtures use simple test-owned stats to exercise presentation uniformly: canonical
Worker cannot Attack and Hobby Knight uses L-shaped movement. No game rule changes.

All five fronts face +Z after an observed positive-Z Move. Side views and opposite
headings show grounding on the base top, proportionate scale, Side rims and correct
turning. Barbarian's axe is retained within its radial allowance. Cleric's staff
and robe hem fit its supporting base. Hobby Knight's wooden horse projects forward
without a fit conflict. Worker's rear sack is visible and does not enlarge logical
picking. Goblin stays small at .55 height and benefits from zoom at distant framing.
None needs a metadata adjustment or Type-specific rendering code.

Captured and inspected PNGs under `.playwright-cli/browser-qa/`:

| Check | Goblin | Barbarian | Cleric | Hobby Knight | Worker |
| --- | --- | --- | --- | --- | --- |
| Close front/side/rear | mSaBYa | Moj7ZS | M1ndP7 | Js50Eu | 1wcHHA |
| Positive-Z Move | FxuJx8 | eIHBtO | ejjQFR | nSxCJC | v9ddWI |
| Attack facing | 4D1v2g | 412VWQ | Rz8a6A | jSroZH | a0OgBR |
| Lying / restored Upright | On9H3Z | NnpbWM | iDYXSM | aP0Xa3 | cJDELm |
| Hidden / entered / left FoW | v6X19P | IEd9Ox | aAaYGO | TYrv4p | IiJcKb |
| Canvas-center picking | CZy3pf | sDEIQU | 9ebGWQ | 4gmrUJ | yvSLPT |

Each table entry is an `inspection-` directory suffix. `inspection-Ax7CZ9` verifies
all seven Types together, blue/red bases, lighting On/Off, perspective 65° angled/
side/rear views, orthographic overhead and 600×850 narrow layout. `inspection-j4L3Cd`
verifies repeated Cardboard/Miniatures and Flat/Classic/Enhanced/Tabletop switches,
retained resources and restart. `inspection-LbCpNz/restored.png` verifies all seven
after reload; its earlier diagnostic snapshot intentionally catches loading fallback.
Local contact sheets and measurement JSON are under `.playwright-cli/batch-models/`.

Final successful plans have no console errors or failed asset requests. Refresh/
scenario replacement can abort the superseded SSE request. An early performance
fixture duplicated unique Heroes on one Side; it was corrected to one per Side.
The initial picking fixture incorrectly retained Troll's Smash Door follow-up;
it now stops after the supplied Stay/selection check. One mixed capture caught a
400 command response during replacement; the fresh repeat above passed. These
were QA fixture/timing corrections, with no runtime/rules validation changes.

### Mixed-scene performance

Headed Edge, 1440×1000, dedicated 12×8 Stone Floor board, all seven production
Types, perspective 65°, lighting On. The two scenes contain one of each Type and
one per Side of each Type respectively. Reset after models are ready, then eight
alternating rotations sample warm CPU submission and frame intervals. Complete
scene counts include terrain, bases and feedback. These are short local CPU/frame
samples, **not GPU timings or sustained/low-end FPS claims**.

| Miniatures | Model triangles | Scene triangles | Draw calls | Warm CPU render p95 ms | Frame interval p95 ms | Camera samples |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 7 | 67,945 | 69,115 | 35 | 2.3 | 16.73 | 14 |
| 14 | 135,890 | 138,012 | 63 | 2.4 | 16.70 | 13 |

Both scenes retain seven shared model geometries, fourteen lit/Off model material
variants, twenty-one shared model textures and one fetch per model. Total renderer
resources are **38 textures / 20 geometries / 8 programs**, with **459 MiB** estimated
scene textures (448 MiB for models). Resource counts remain constant when doubling
instances and after renderer cycles. Ordinary model clones do not duplicate maps
or geometry. Repeated switches retain ready cached sources without refetches.

| Model | Fetch / decode / total ms, 7-figure scene | Fetch / decode / total ms, 14-figure scene |
| --- | --- | --- |
| Goblin | 78.7 / 362.4 / 441.4 | 168.6 / 336.7 / 505.4 |
| Barbarian | 127.5 / 304.5 / 432.1 | 171.1 / 322.5 / 493.7 |
| Cleric | 126.3 / 288.8 / 415.3 | 161.9 / 323.4 / 485.5 |
| Hobby Knight | 127.0 / 277.6 / 404.6 | 170.9 / 294.5 / 465.5 |
| Worker | 126.7 / 295.6 / 422.3 | 165.0 / 346.5 / 512.1 |
| Zombie | 153.6 / 301.3 / 455.1 | 206.0 / 313.2 / 519.3 |
| Troll | 154.3 / 756.1 / 910.4 | 208.5 / 646.4 / 854.9 |

Evidence: `inspection-XNXTaI` and `inspection-188wOZ`, `warm.yaml` and warm PNGs.
Texture memory is an uncompressed RGBA+mipmap estimate, not measured driver VRAM.
The large seven-Type texture budget is the primary remaining cost; no destructive
optimization was warranted by these CPU samples. Browser cache and concurrent
decode affect load timings; this is not an isolated network benchmark.

### Automated verification and remaining limits

Focused asset/catalog/model/base/viewport tests pass. New checks cover exact
registrations/dimensions, paid provenance and references, original/runtime and move
hashes, deterministic preparation, geometry/map preservation, independent deferral,
overwrite refusal, successful import and repeat idempotency including unselected
arrivals. Five actual-geometry viewport cases verify shared clones, grounding,
facing with stationary bases, both projection pick paths, Posture restoration,
lighting, explored-column clipping across hidden gaps, switching and cleanup.
Node CPU geometry tests strip image bindings only in temporary memory; original
texture decode/material handling is verified separately in Pillow and Edge.

All 567 client/security tests and the .NET regression suites pass (1,601 Engine + 384 Web =
1,985 .NET tests). Debug and Release solution builds are warning-free. Actual
Release publish checks cover both test-fixture and ordinary packaging: exactly
seven production GLBs, complete catalog/local loader graph, no source assets or
QA registration in shipping output. All seven reproduction checks pass.

Remaining manual gaps are frame-exact transient FoW pixel assertions, native touch,
real-driver injected context loss/network failure, sustained GPU and low-end-device
measurements. Existing/new CPU tests cover their relevant geometry/lifecycle/failure
contracts, but do not establish those hardware behaviors. At distant/narrow framing
small figures and equipment lose detail; normal zoom remains available. Tangent
portability and unknown generation settings remain recorded uncertainties.

Changed files: the five source GLBs/READMEs/provenance files and runtime GLBs;
`unit-visuals.json`; inbox README; `tools/import-unit-models.cjs`,
`tools/batch-model-qa-plans.cjs`, `tools/test-miniature-publish.cjs`;
`batch-models.test.cjs`, `three-viewport.test.cjs`, `zombie-model.test.cjs`,
`MiniatureHostingTests.cs`; repository README and this document. Existing supplied
reference artwork is used in provenance, without modification.
**No Engine or server observation implementation files changed.** No renderer
implementation, rules, logical footprint, authored scenario or `CONSTRAINTS.md`
changes. Zombie/Troll source/runtime assets and registrations remain unchanged.

## Remaining 19 Tripo miniatures — 2026-10-28

Historical integration snapshot. Current runtime sizes, hashes and texture budgets
are recorded in [Runtime texture standardization to 1024](#runtime-texture-standardization-to-1024--2026-10-28).

All 19 supplied models passed and are approved ordinary production miniatures:
Bomb Imp, Castle Guard, Chief Mechanic, Displacer Demon, Geomancer, Goblin Bishop,
Goblin King, Goblin Peasant, Grunt, Labyrinth Shaper, Rogue, Royal Bishop, Royal King,
Royal Peasant, Royal Queen, Shaman, Skeleton Archer, Witch Queen and Wizard.
Production total is **26**; none deferred. Initial working tree was clean. All
inbox GLBs mapped exactly to canonical `<slug>-type` IDs. The importer moved each
success into its archive only after exclusive writes, review/hash checks and
registration. Inbox now contains only its README; unrelated files were preserved.
The seven earlier model descriptors, sources and runtime exports are unchanged.

### Assets, compatibility and provenance

For each slug: source `assets/source/models/units/<slug>/original.glb`, reviewed
`provenance.json` and README beside it; runtime
`ProjectDelve.Web/wwwroot/art/models/units/<slug>/v1/<slug>.glb`. Normal catalog
registration is `<slug>/v1`, export version `tripo-v1`, canonical Type
`<slug>-type`. That export version does not identify an inferred Tripo model.
Source total **83,036,920 bytes**, prepared total **70,743,464 bytes**; **179,962
model triangles**. Every original and prepared SHA-256, equal before/after-move
source hash, byte count and exact source/runtime path is recorded per asset.

All have one static identity root, mesh, triangle primitive and opaque PBR material;
finite POSITION/NORMAL/UV data, minimum Y zero and positive determinant. No skins,
animations, cameras, external resources or actual extension payloads. The 12,000
triangle and two-primitive safety limits are unchanged. Runtime descriptor image
budget is 1024; source inspection retains the existing 2048 input budget.
Khronos glTF Validator **2.0.0-dev.3.10** reports **zero errors for all 38 original/
runtime validations**, one generated-tangent-space warning per file. Original
unused FB_ngon_encoding information is removed by preparation. All **114 source/
runtime images** fully decoded in Pillow; all runtime maps also decoded in Edge.
Generated tangents remain a portability advisory. Eighteen models exceed the
advisory 8,000 triangles; Witch Queen has 7,982. Transfer sizes still exceed the
preferred budget, without violating the hard contract.

All **targetHeight, maximumRadius and baseRadius values are unchanged**. No hard
radius incompatibility occurred, no model was distorted, and all logical
footprints remain 1x1. Royal King's low cape leaves .033071 Cell contact margin,
.006929 short of the preferred .04; actual .286929 radius fits its .32 base and
.40 hard allowance. This is advisory and does not justify changing metadata.

| Slug | Source / runtime bytes | Triangles | Height | Actual / allowed radius | Base radius | Contact margin |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
| bomb-imp | 3,959,800 / 3,538,080 | 9,686 | 0.48 | 0.140792 / 0.28 | 0.24 | 0.099208 |
| castle-guard | 4,985,892 / 4,098,848 | 9,486 | 0.85 | 0.241564 / 0.43 | 0.34 | 0.117241 |
| chief-mechanic | 4,118,084 / 3,575,316 | 9,491 | 0.73 | 0.166323 / 0.4 | 0.31 | 0.143677 |
| displacer-demon | 3,664,392 / 3,345,168 | 9,621 | 0.98 | 0.229132 / 0.44 | 0.38 | 0.169524 |
| geomancer | 3,979,992 / 3,559,332 | 9,064 | 0.75 | 0.222927 / 0.38 | 0.31 | 0.131907 |
| goblin-bishop | 4,285,600 / 3,663,172 | 9,534 | 0.65 | 0.170698 / 0.34 | 0.28 | 0.127981 |
| goblin-king | 4,626,160 / 3,959,616 | 9,416 | 0.68 | 0.232973 / 0.35 | 0.3 | 0.114005 |
| goblin-peasant | 4,930,644 / 4,037,536 | 9,482 | 0.55 | 0.136749 / 0.32 | 0.27 | 0.137225 |
| grunt | 4,957,448 / 3,898,076 | 9,413 | 0.78 | 0.261202 / 0.42 | 0.34 | 0.117170 |
| labyrinth-shaper | 3,786,568 / 3,521,404 | 9,386 | 0.85 | 0.223506 / 0.4 | 0.33 | 0.106494 |
| rogue | 3,694,768 / 3,255,080 | 9,724 | 0.73 | 0.191604 / 0.36 | 0.3 | 0.142652 |
| royal-bishop | 4,172,584 / 3,691,788 | 9,476 | 0.85 | 0.194069 / 0.38 | 0.31 | 0.144090 |
| royal-king | 4,397,552 / 3,742,112 | 9,982 | 0.83 | 0.286929 / 0.4 | 0.32 | 0.033071 |
| royal-peasant | 3,803,180 / 3,250,292 | 9,832 | 0.7 | 0.152827 / 0.36 | 0.3 | 0.147173 |
| royal-queen | 5,000,436 / 4,265,064 | 9,702 | 0.83 | 0.244687 / 0.4 | 0.32 | 0.075313 |
| shaman | 4,959,892 / 4,015,216 | 9,401 | 0.68 | 0.171510 / 0.36 | 0.29 | 0.144795 |
| skeleton-archer | 3,639,420 / 3,356,972 | 9,484 | 0.73 | 0.184746 / 0.36 | 0.29 | 0.143180 |
| witch-queen | 6,074,996 / 4,572,140 | 7,982 | 0.73 | 0.240341 / 0.4 | 0.31 | 0.069659 |
| wizard | 3,999,512 / 3,398,252 | 9,800 | 0.8 | 0.210156 / 0.38 | 0.32 | 0.161439 |

The owner confirms paid Pro generation for this exact batch. Provenance reuses
the established paid-user rights review, with one shared check of
[Tripo terms](https://www.tripo3d.ai/terms) (still 2025-07-11, 5.2.2) and
[Pro commercial-use pricing](https://www.tripo3d.ai/pricing). Restrictions and
third-party-rights limits remain recorded. Generation-time supplemental terms,
model/version, date, settings, selected inputs and export receipt are unknown.
Existing project portraits/reference sheets are recorded without inventing input
history, public asset licensing or exclusive copyright.

### Short texture investigation and reusable preparation option

All three maps have a role: base color in both material variants; normal and
metallic/roughness in Lighting On. Lighting Off uses the shared base color and
does not free the other maps. There is no requirement to keep all three at 2048.
At inspected normal Tabletop zoom and close detail zoom, **1024-square maps are
sufficient**: bow, thin limbs, staff/crystals, large hats, horns, armor and costume
detail remain readable. This is visual approval, not a formal image-diff claim;
extreme zoom can expose reduced texel detail.

The existing preparation path now accepts optional `--texture-limit 1024` in
`import-unit-models.cjs`, recorded as `preparation.textureLimit`. One generic
offline helper uses installed Pillow: fully decode, aspect-preserving LANCZOS,
lossless PNG at compression 9, aligned buffer-view repack. No upscaling. Geometry
bytes, materials, bindings and originals remain unchanged. Omitted configuration
reproduces all seven earlier exports exactly. Limits 256/512/1024/2048 are
configurable; no per-Type renderer branches, new browser dependencies, model
framework or compression service. Reproduction uses Python **3.14.7**, Pillow
**13.0.0**, zlib **1.3.1.zlib-ng**; configure `PROJECTDELVE_PYTHON` if Python is not
on PATH. These are offline preparation/test prerequisites, not shipping/runtime
requirements. Reviewed hashes and the limit must match before apply.

Current runtime preflight accepts only core static GLB and embedded PNG/JPEG,
rejecting extension declarations. No KTX2/Basis transcoder or compressed-texture
loader is configured. PNG/JPEG reduce transfer bytes, not decoded GPU allocation.
GPU texture compression would require a separately justified preparation/loader
change; none is introduced here.

Estimated RGBA plus mipmaps falls **64 → 16 MiB per new model**, **1,216 → 304 MiB
for these 19** (75%, 912 MiB saved). Existing seven remain 448 MiB of models,
approximately 459 MiB in the earlier scene. Loading all 26 distinct models would
retain **752 MiB for model maps**, plus board/portrait textures; the catalog does
not eagerly load unused models. Additional instances share textures/geometry.
Distinct loaded sources accumulate by URL/version until viewport disposal:
removing figures, changing scenario or switching representation does **not** evict
ready sources. Viewport disposal disposes unique geometry/material pairs/textures
and closes image bitmaps; late loads are disposed too. Existing CPU lifecycle tests
verify this ownership. Browser reload/context teardown releases the old context;
driver-level VRAM reclamation timing was not measured. No new eviction policy.

### Restricted Edge visual and gameplay evidence

Isolated Production host on **5081**; existing **5080 PID 30892** preserved. All
plans use the protected dispatcher without permission/runner changes, fresh Edge
contexts, ordinary production registrations and test-owned scenarios. Generate
with `node tools/batch-model-qa-plans.cjs --remaining`. Move/Attack/picking
fixtures retain the previous simple test-owned stat overrides; no game rules or
real/playtest scenarios are edited. Canonical detail/mixed/posture fixtures retain
real Types. Four camera directions were captured and inspected for **every model**.
All model-specific findings and evidence are in each provenance record/README.

| Slug | Four-direction scan | Positive-Z Move |
| --- | --- | --- |
| bomb-imp | eSegGY | qg3wKF |
| castle-guard | dhOVEH | sampled below |
| chief-mechanic | ulAWu9 | sampled below |
| displacer-demon | Mm2rUl | Bjms5y |
| geomancer | 1I9CIA | 6A0Kot |
| goblin-bishop | eh7OIo | p9wJ6R |
| goblin-king | HwtJEz | sampled below |
| goblin-peasant | NFjjWJ | sampled below |
| grunt | of6lgA | sampled below |
| labyrinth-shaper | bi15R0 | sampled below |
| rogue | xJfZfd | sampled below |
| royal-bishop | mrpRzU | sampled below |
| royal-king | 2hIpB7 | LGbapS |
| royal-peasant | gTa02E | sampled below |
| royal-queen | IcTk4Q | vP8ZDw |
| shaman | 2Thlg9 | sampled below |
| skeleton-archer | azzpSn | sk2qWh |
| witch-queen | 3YXsXO | sampled below |
| wizard | b25CVo | sampled below |

Entries are `inspection-` directory suffixes below `.playwright-cli/browser-qa/`.
Representative interaction captures:

| Type | Attack facing | Lying/restored | Hidden/entered/left FoW | Canvas picking |
| --- | --- | --- | --- | --- |
| bomb-imp | CwC2xI | G4mi1O | D7W8zw | fTfUST |
| skeleton-archer | bGivUF | WsgcVx | gVW60F | duiq8L |
| royal-king | yp95Bf | nvmvvI | pPRIEr | aAXAsg |
| royal-queen | CioDN2 | GBe494 | 801LAw | 3y1EJa |
| goblin-bishop | vJqwDP | bh1ytK | y0ulII | 1Edc9v |
| geomancer | leJyAS | KQolBw | U1Buuy | 12GB5C |
| displacer-demon | 1reX0b | X3Cgjw | a87cmr | nMXuMy |

Bomb Imp stays small at .48 height; Skeleton Archer retains bow/thin limbs;
Royal costumes and Goblin Bishop headgear are intact; Geomancer's staff/crystals
and Displacer Demon's horns/armor survive. Contact, grounding at base top, Side
colors, facing after observed Move/Attack, logical picking, Lying Cardboard
fallback and restored miniature, FoW absence/reappearance and camera following
were inspected. CPU actual-geometry tests now exercise all 19 in both projection
pick paths, all yaw quadrants, lighting and discontinuous FoW movement clipping.

Mixed capture **fY1GPM** covers 38 figures, perspective/orthographic, angled/side/
rear/overhead, Lighting On/Off and 600x850 narrow layout. Its early captures show
some expected loading fallback; later side/rear/narrow views show loaded models.
**6Yt1MD** covers repeated Cardboard/Miniatures and Classic/Enhanced/Flat/Tabletop
switches, cache reuse and restart. Initial reload **INqCp1** caught loading;
**oPstXB/restored.png** verifies all 38 loaded after reload. **e2vUvy** verifies
fully loaded Lighting Off and a narrow layout; its Developer drawer intentionally
overlays the board, while fY1GPM shows ordinary narrow controls. No console errors
or failed asset requests. Refresh/scenario replacement aborts superseded SSE
requests as expected. Local QA maps, validation reports, timing JSON and reviewed
contact sheets are in `.playwright-cli/remaining-models/` (ignored evidence).

### Bounded mixed-scene measurements

Headed Edge, 1440x1000, test-owned 12x10 (19) / 12x16 (38) StoneFloor boards,
19 distinct models, perspective 65 degrees and Lighting On. One figure per Type,
then one per Side per Type. Reset timing and eight alternating camera rotations.
All model cache entries are ready. Counts include board, bases and feedback.

| Figures | Draw calls | Scene triangles | CPU render p95 ms | Frame interval p95 ms | Camera samples | Evidence |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 19 | 83 | 182,812 | 1.7 | 16.7 | 12 | SyBEpy |
| 38 | 159 | 365,502 | 14.6 | 16.8 | 17 | IdFTpT |
| 38 repeat | 159 | 365,502 | 2.1 | 16.7 | 12 | e2vUvy |

The first 38 sample has a high p95 but 2.0 ms median; repeat median 1.9 ms. Small
sample counts and startup/browser scheduling make these variable. These are CPU
submission/frame-interval measurements, **not GPU timing, sustained FPS or low-end
performance claims**. All scenes retain **331.92 MiB estimated textures**, **97
uploaded textures, 35 geometries and eight programs** in the warm lit snapshot.
There are 19 shared model geometries, 38 lit/Off material variants and 57 shared
model textures (304 MiB), one fetch per model independent of instance count.
Other texture memory includes loaded portraits/glyphs/board. Scene material count
115 (19 figures) / 119 (38) includes Side variants. Lighting Off uploads additional
program variants without duplicating maps. Switch/restart keeps cached sources.

19-figure fetch/decode/ready ranges: **54–361 / 569–895 / 844–1,157 ms**;
38 figures: **230–1,080 / 462–1,111 / 1,093–1,581 ms**. Concurrent decode and
localhost/browser cache affect these figures; they are not isolated network
benchmarks. Exact per-model timings are in the local performance JSON.

### Automated verification and remaining gaps

Focused asset/catalog/importer/viewport tests and follow-up checks pass. Coverage
binds every new registration to original/runtime hashes and unchanged dimensions,
compares geometry buffer bytes/accessors/hierarchy/material bindings, reproduces
1024 exports, checks optimized importer review/hash mismatch rejection and
exclusive/idempotent writes, and exercises actual geometry sharing, grounding,
facing, picking, Posture, FoW clipping and disposal. Existing Zombie/Troll and first
batch tests still pass; fallback tests now use a missing test-owned Type instead
of assuming Grunt remains unregistered. Source/runtime reproduction passes for
all **26** exports. The optional preparation dependency is required for optimized
reproduction tests; normal .NET builds/publish do not invoke it.

Full client/security suite: **589 passed**, zero failed/skipped. Full .NET suite:
**1,601 Engine + 384 Web = 1,985 passed**, zero failures/skips. Debug and Release
solution builds: **zero warnings/errors**. Actual Release publish, both fixture
and ordinary: **exactly 26 production GLBs**, exact catalog/loader assets and
provenance-bound runtime hashes/budgets, no source assets or QA fixtures in normal
shipping output. Repeat importer on the empty inbox does no work.

Remaining manual gaps: native touch, frame-exact transient FoW pixel assertions,
injected real-driver context loss/network failures, measured GPU/driver memory
reclamation, sustained/low-end performance and formal 2048/1024 image-diff quality
comparison. Very small/distant/narrow models naturally lose readable equipment
detail. CPU geometry/lifecycle/failure tests cover the corresponding contracts
where applicable. **No Engine or server observation implementation files, gameplay
rules, CONSTRAINTS.md or runtime renderer implementation files changed.**

## Runtime texture standardization to 1024 — 2026-10-28

All **26 production miniatures** now use a maximum embedded runtime texture
dimension of **1024**. Zombie, Troll, Goblin, Barbarian, Cleric, Hobby Knight and
Worker were reprepared from their archived originals using the existing
`tools/prepare-unit-model.cjs` pipeline and `preparation.textureLimit: 1024`.
The other 19 runtime hashes remain byte-for-byte unchanged. All 26 source GLBs
also match the pre-task hashes; none was rewritten.

### Runtime bytes and hashes

Every row retains all three channels: base color, metallic/roughness and normal.
Each channel changes from 2048x2048 to 1024x1024, preserving aspect ratio. JPEG
inputs are decoded and the resized runtime maps use lossless PNG compression 9.

| Model | Previous bytes | New bytes | New runtime SHA-256 |
| --- | ---: | ---: | --- |
| zombie | 4,466,452 | 3,789,900 | `07fb0b8d3fee6139503f0fd8df375c93f1e7bb335fa18589c76760a28b135e24` |
| troll | 4,370,372 | 3,522,536 | `9bc1970d2ca0d51c3b14693b068c3b8f6f305a86df6e2a2e24ba7b8974f51274` |
| goblin | 5,008,720 | 4,032,532 | `448c1b490d8e8d477451648fc42d36be3c1fc919286b3ebd5c8c19ed9d8db8d0` |
| barbarian | 4,449,268 | 3,731,072 | `71674c266f171b3254a5fb946dcf35c03268670989608429dd87d573cd759ea0` |
| cleric | 4,160,028 | 3,487,136 | `bc941a1907379cc56c7f6e900f8b5c5f3bff18107c730289a431e421afb44ecb` |
| hobby-knight | 4,400,276 | 3,870,972 | `17d755a88cbd439a91f04681e7a6fdc5ab82884f1a1bdfaec732e998ce48455d` |
| worker | 4,343,160 | 3,585,440 | `2f75f4ed1165b80cb7f922fbab4ee0ef0d53822d10a0a228ee3de58f6fbfed2d` |

Total transfer bytes: **31,198,276 → 26,019,588**, a reduction of **5,178,688
bytes (16.6%)**. Provenance retains each previous runtime hash/size alongside
the new hash/size, source-preservation evidence and encoder versions. Original
inspection records continue to describe the unchanged 2048 source maps.

Estimated decoded RGBA plus mipmaps: **64 → 16 MiB per model**, **448 → 112 MiB
for these seven**, saving **336 MiB (75%)**. Without mipmaps the seven-model
estimate is 336 → 84 MiB. All 26 distinct models together would use approximately
**416 MiB** for model maps (previously 752 MiB), plus board/portrait textures.
These are dimension-based estimates, **not measured GPU allocations**.

### Preservation and preparation verification

The existing helper fully decodes each image and uses aspect-preserving LANCZOS.
All 21 new maps were fully decoded and compared with the expected resized pixels.
All source/runtime images in this batch are RGB; no alpha channel existed to
remove. OPAQUE material semantics, all texture slots/samplers, geometry buffer
bytes, UVs/accessors, hierarchy, orientation and transforms remain identical.
Catalog edits change only seven descriptor texture budgets from 2048 to 1024;
heights, radii, bases, model URLs/versions/references and Unit rows are unchanged.

Khronos glTF Validator **2.0.0-dev.3.10**: all seven sources and seven runtimes
have zero errors, one existing generated-tangent-space warning per file, and no
new warnings. Runtime assets are core static glTF with no external resources or
extension declarations/payloads. All 26 preparation/provenance tests pass;
the seven provenance-driven `--check` commands also reproduce the runtime bytes.
Python **3.14.7**, Pillow **13.0.0**, zlib **1.3.1.zlib-ng**, Node **v22.23.2**
match the recorded preparation environment. The pipeline itself is unchanged.

Focused model/import/provenance tests: **10 passed**. Focused Web
`MiniatureHostingTests`: **6 passed**. Release Web build: zero warnings/errors.
`node tools/test-miniature-publish.cjs --release` passes fixture and ordinary
publishing; ordinary output contains exactly the 26 approved GLBs with exact
provenance-bound hashes, catalog and loader assets, no original GLBs or QA assets.
The full client/.NET suites were not run for this asset-only task.

### Restricted Edge visual evidence and resource counts

Controlled before/after, test-owned seven-model scene, 1440x1000 headed Edge:
**inspection-KSgUYW** (2048 baseline) and **inspection-5y0TpH** (1024).
The baseline used copied static files under an ignored QA root, with the seven
saved pre-task runtime GLBs; tracked runtime files were never swapped back.
Normal Tabletop zoom with Lighting On/Off shows no noticeable loss of skin,
clothing or painted detail. Extra angled views **inspection-bVCuSi** (before)
and **inspection-UnesCR** (after) preserve costume colors, equipment and normal
lighting response. Camera tween timing varies slightly between captures, so
these are qualitative comparisons, not pixel-diff measurements.

Both controlled scenes have **35 draw calls, 69,115 scene triangles, 38 uploaded
textures, 20 geometries, 56 scene materials**, and **8/10 programs (Lighting
On/Off)**. All seven models are ready, each with one fetch, one geometry, two
material variants and three textures. Estimated whole-scene textures change
**459 → 123 MiB**, exactly the 336 MiB model reduction. No unexpected model or
settled-scene resource changes. An earlier cold-host capture included additional
startup scene resources; controlled repeat captures removed that discrepancy.

**inspection-4fb5bn** shows all seven upright beside seven Lying Cardboard
fallbacks (test-owned Undying definitions), with both lighting settings.
**inspection-mLHimf** confirms Cleric facing after positive-Z movement;
**inspection-Sc1bnf** confirms representative FoW hidden → entered → hidden.
These successful captures have no console errors or failed model requests;
expected SSE aborts occur when scenarios are replaced/restarted. An initial
Lying fixture lacked Undying and was rejected; the corrected dedicated fixture
passed without production changes. PNGs, plans, baseline hashes, image-decode
evidence and full validator reports are under ignored `.playwright-cli/`.

No remaining asset issues were found. Extreme-zoom texture fidelity and measured
GPU allocations were not benchmarked. The existing tangent warning and contact
advisories remain unchanged; this task does not adjust geometry or visual metadata.

## Final six miniature diagnosis and import — 2026-10-28

Production now has **31 approved miniatures**. Royal Knight, Locomotive, Tower
Troll, Ghost and Diamond Kart use ordinary production catalog registrations.
Red Dragon remains unchanged in the inbox and unregistered. Complete preflight,
hashes, dimensions, per-model reasoning, provenance, visual evidence and remaining
limits: [final-six report](assets/source/models/FINAL_SIX_IMPORT.md),
[preflight JSON](assets/source/models/final-six-preflight.json), and
[performance JSON](assets/source/models/final-six-performance.json).

All six sources were measured before asset/catalog modifications: one static
mesh/primitive/material, three 2048² maps, 8,703–9,852 triangles, finite identity
transforms, Y-minimum zero, no external resources, animation, skins or morphs.
Khronos Validator 2.0.0-dev.3.10 reports zero errors on all originals/prepared
exports, one existing generated-tangent warning each. Unused source extension
declarations are removed; no actual extension payload exists. All 36 source and
prepared maps fully decode. Accepted runtimes retain all three channels at 1024²;
pixels match the expected LANCZOS downsample. No source or existing model changed.

| Model | Height | Actual radius | Allowed radius | Contact / base radius | Outcome |
| --- | ---: | ---: | --- | --- | --- |
| Royal Knight | .85 | .417159 | .44 | .228755 / .36 | Approved, rigid orientation correction |
| Locomotive | .58 | .484682 | .45 → .485 | .472310 / .39 | Approved, modest radius allowance |
| Red Dragon | 1.45 | 1.196189 | .88 | 1.196189 / .72 | Deferred, beyond .92 renderer ceiling |
| Tower Troll | 1.20 | .374053 | .44 | .307478 / .43 | Approved unchanged |
| Ghost | .83 | .273098 | .36 | .273098 / .30 | Approved unchanged |
| Diamond Kart | .53 | .449056 | .45 | .403133 / .37 | Approved unchanged |

Contact estimates include the lowest 7.5% of height, not only load-bearing soles.
Locomotive's low cowcatcher and Kart's curved tires inflate that estimate; actual
lowest .005-Cell band radii are .279421/.343526, inside their .39/.37 bases.
Ghost misses the preferred .04 contact margin by .013098. All are advisory;
screenshots show supported wheels, cloth and the Knight's rear hooves. No base,
height, pose, wheel, wing, costume or chassis was changed to pass.

Royal Knight's horse faces **−X in the original**. The existing offline exporter
now optionally adds a rigid scene wrapper using reviewed
`preparation.rotationYDegrees` (0/±90/180); omitted option reproduces previous
exports exactly. Knight uses **+90° Y**, preserving geometry buffers, authored
nodes, height and radius, to correct to +Z forward. Prepared dimensions are
.403226 × .85 × .824675. Importer `--rotation-y 90` binds the reviewed angle and
hashes before exclusive writes; source and runtime inspections are recorded
separately. No runtime renderer change or per-Type orientation branch.

Locomotive's largest radius is a rear cab corner, while the cowcatcher extends
low beyond the circular base. Its **.035-Cell metadata increase** fits the existing
generic .92 limit and remains below half a Cell. Existing actual-radius FoW
clipping, logical picking and public camera fitting apply unchanged. This does
not enlarge the existing Troll-driven camera overhang. Full locomotive geometry
is retained; longitudinal extent is .944619 Cells.

Dragon is an actual canonical **2×2 Unit**, with head, wings, tail and four legs.
Its **2.360672 × 1.45 × 1.041773** source-axis normalized bounds do not fit the
current envelope. Low tail at (−1.180336,.019159,−.194103) reaches radius 1.196189;
962 source POSITION entries exceed .88. Even metadata 1.197 would fail the generic
.92 schema/runtime ceiling. Radius >1 can require more than three FoW columns on
an axis during movement, exceeding the existing fixed 3×3 copy guarantee.
Elevated wing/tail clipping and larger camera bounds cannot be approved as-is.
Shrinking to the generic ceiling would reduce height about **23.1% to 1.115**;
that substantive appearance change was not made. No wing removal, tail shortening,
geometry deformation or global safety-limit weakening. Diagnostic anatomy preview
7ekQXK used only an ignored static-root copy at height 1.0, the real 2×2 footprint
and .72 base; it is not authored-height approval. Later integration needs an
explicit presentation/renderer or new-source decision. Dragon remains Cardboard.

`node tools/batch-model-qa-plans.cjs --final` generates test-owned data-only Edge
plans. Ordinary 5081 production host, existing **5080 PID 40312 preserved**.
Four-direction shape and positive-Z Move, Attack-facing, Lying/restoration,
picking and FoW entry/exit were captured and inspected for **every accepted model**;
the full per-model evidence matrix is in the report. Mixed **3LIu03** verifies
both projection/lighting modes, Side bases, multiple angles and 600×850 narrow
layout. **DdYme4** verifies renderer/representation switching and restart;
**7PH3tJ/restored.png** verifies ten ready miniatures after reload. **az9J21**
also shows fully loaded Off/narrow modes. An invalid initial canonical Kart on
StoneFloor was fixed in the dedicated fixture to use Asphalt; production content
was not changed. Successful runs have no console errors or failed model requests;
SSE aborts during scenario replacement/refresh are expected.

Headed Edge, 1440×1000, test-owned 12×8 Asphalt board, five distinct new models,
Perspective 65°, Lighting On. Timing reset after ready; eight alternating camera
rotations. Counts include board/bases/feedback. Short bounded samples:

| Figures | Draw calls | Triangles | Warm CPU render p95 ms | Frame interval p95 ms | Camera samples | Evidence |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 5 | 27 | 48,001 | 1.9 | 16.73 | 13 | Ae9Aet |
| 10 | 47 | 95,784 | 2.0 | 16.70 | 12 | az9J21 |

Both retain **88.17 MiB estimated textures**, **28 uploaded textures, 16 geometries,
8 programs**, scene materials 46/50. Shared models: **5 geometries, 10 lit/Off
variants, 15 textures, 80 MiB**, one fetch per URL. Off adds programs without
duplicating maps. Fetch/decode/ready ranges for five figures: 112.8–115.7 /
299.1–401.7 / 414.8–514.7 ms; ten: 125.9–126.9 / 125.5–240.5 / 251.7–367.2 ms.
These are CPU submission/frame intervals and dimension-based texture estimates,
**not GPU performance, measured VRAM, sustained FPS or low-end claims**. All 31
distinct models would estimate 496 MiB of model maps; unused models are not
eagerly loaded. Existing cache/disposal behavior is unchanged.

Focused asset/importer/metadata/viewport checks pass, including rigid wrapper
preservation, reviewed-angle rejection, geometry sharing, both projection pick
paths, grounding, lighting, Lying, transient FoW and disposal. Strict baseline
hash tests preserve the previous 26 source/runtime/provenance files. Dragon
deferral, unchanged source and missing registration are tested. Full
client/security: **598 passed**. Full .NET: **1,601 Engine + 384 Web = 1,985 passed**.
Focused hosting/catalog: **7 passed**. Debug/Release solution builds: **zero
warnings/errors**. Actual Release fixture and ordinary publish checks pass;
shipping output has **exactly 31 production GLBs**, exact provenance-bound hashes,
catalog/loader and no original sources/QA assets.

Remaining incompatibility: Dragon at authored height. Existing tangent warning
and contact advisories remain. Native touch, frame-exact transient FoW pixel
checks, measured GPU allocation/reclamation and sustained/low-end performance
remain unmeasured. **No Engine or server observation implementation files,
scenario formats, CONSTRAINTS.md, existing approved models or runtime renderer
implementation files changed.**

## Red Dragon production completion

All **32 canonical Units** now have production miniatures. Red Dragon preserves
its original GLB and 1.45-Cell height, with radius budget 1.20, circular base .90,
1024 textures and a reviewed rigid +90-degree Y facing correction. Generic model
limits are .92 for 1x1 and 1.25 for 2x2; transient FoW uses nine copies for smaller
envelopes and sixteen when required. Import diagnostics use explicit canonical
footprints. Existing models, gameplay and observations are unchanged.

The earlier Dragon deferral entries are historical. Current provenance, source
preservation, Edge evidence, regression/publish results and limitations are in
[the completion report](assets/source/models/FINAL_SIX_IMPORT.md#red-dragon-completion--2026-10-28).
