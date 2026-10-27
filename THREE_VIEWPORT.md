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
`updateLabels` projects those values. A future local model factory can replace
the body and supply different bounds/anchors without owning Unit identity,
choices, cards, movement legality or state. No model loader/configuration system
is implemented.

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
geometry is unchanged. Tilt is limited to 0–55°. Full fits border and .75 Cell of
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
and .75 Cell, with 28px padding. It does not assume bounds vary linearly with
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
the current .75 Cell Full-height budget remain; future taller body factories can
extend that presentation budget without gameplay changes.

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
