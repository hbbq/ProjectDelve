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

The approved Tripo H3.1 Zombie is registered in the normal production catalog.
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
| Texture limit | Default 1024; explicit asset-specific budget may allow larger embedded PNG/JPEG (approved Zombie: 2048) |
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
     "maxTriangles": 12000, "maxPrimitives": 2, "maxTextureDimension": 2048 }
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
exactly one `{typeId,targetHeight,maximumRadius}` entry; optional `model` references
an approved asset's `id`. Asset entries contain `id`, `approved:true`, local `url`,
`version`, and optional `maxTriangles`, `maxPrimitives`, `maxTextureDimension`.
Footprints, HP, movement and other gameplay content are not duplicated in JSON.

All **32** current canonical Types are included, including royal/goblin pieces,
Diamond Kart, Locomotive, Chief Mechanic, Worker and Red Dragon. Terrain Machines
are not Unit Types. Initial heights range from .65 (Bomb Imp) through .80 (Goblin),
1.20 (Zombie), 1.90 (Troll), 2.10 (Tower Troll), to 2.60 (2×2 Red Dragon). Vehicles
start lower (.75 Kart/.85 Locomotive). These authored presentation defaults are
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
explicit visual row with provisional positive finite height/radius, then run
`UnitVisualCatalogTests` and `unit-visuals.test.cjs`. Attach an approved asset later
by referencing `models[].id`. No name-derived runtime dimensions hide omissions.

Height means exact intended model height above its feet, excluding the procedural
.06-Cell base. Radius is a maximum of transformed vertices about the ground-center
after uniform scaling. Radius failure keeps Cardboard; the loader never shrinks to
fit or stretches axes separately. V1 radial budgets stay within the generated
base envelope (.46 × canonical Cell span), retaining the nine-column FoW pool
guarantee. There is no universal 1×1 height cap. A tall compact Troll is valid.
Broader silhouettes outside that envelope require a deliberate clipping/base
budget extension before approval.

The cache keys **source** resources by approved URL/version and measures their
transformed bounds once. Height, radius and Cell span are per-Type contracts, not
source cache keys. Instance wrappers clone the source hierarchy and apply a private
uniform scale/ground offset; geometry, textures and immutable lit/Off material
pairs remain shared. Two Types can reference the same asset at 1.20/.38 and
2.40/.76, respectively, with one download/parse. A Type-specific radius failure
does not poison a ready source needed by another Type. Source triangle/primitive/
texture budgets must agree for a shared URL/version.

Camera fitting uses the public maximum registered height plus base/label allowance
(minimum 1.5), independent of hidden Units and async arrivals. Coarse picking stays
on observed logical footprints and vertical proxies. Miniature labels use their
information anchor and also clear the projected base's top edge, preventing name
plates covering heads at overhead. LOD and width continue using the base.

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
uses the displayed explored set and nine reusable Cell-column copies. Hierarchy
copies preserve nested matrices and material arrays; every mesh gets private
clipping variants of both PBR and Off materials. Planes remain world-aligned while
all facing pivots share yaw. The validated base/radial envelope bounds intersecting
columns to four for 1×1 and nine for 2×2. Height does not weaken vertical planes.
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
