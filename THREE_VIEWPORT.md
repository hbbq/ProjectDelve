# Three.js Flat 2D Playtest viewport

Playtest offers Classic, Enhanced 2D and Three.js Flat 2D under Tools → Display.
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
top. There are no lights, shadows, tone mapping, perspective, tilt or rotation.

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
projection. Unknown covers conceal terrain/objects and transient movement.
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

Future Tabletop mode can add sidewalls/board thickness and a camera orientation
inside these scene groups. Full 3D can replace artwork children with models.
Angled-view concealment of elevated transient geometry needs separate visual
design. None requires changing authoritative gameplay or scenario serialization.

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
