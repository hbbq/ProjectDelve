# Tabletop local lighting V1

`three-local-lighting.js` supplies a shared, observation-derived RGB irradiance
field. Lava has a data recipe (linear color, strength and radius). Contributions
fall off smoothly with horizontal distance and receiver height, accumulate with
a brightness ceiling, and illuminate terrain, Lambert props and imported model
materials. Existing key/fill lights and printed artwork retain their behavior.
Lighting Off and Flat presentation disable the field's shared uniform. Lava's
authored texture remains bright.

The field is rebuilt only when observed terrain, sources, dimensions or edges
change. Ordinary Unit movement samples it in world space, including instanced
geometry, Facing transforms and clipped playback copies. Shader hooks compose
with Ghost treatment. It schedules no frames, owns one texture, reuses it across
checkpoints, and releases allocations on dimension changes and disposal.

For another terrain emitter, add a recipe. For observed objects, Units or effects,
extend the source collection passed to `lighting.local.update(data, sources)`;
sources have `{x, z, color: [r,g,b], strength, radius}` in board coordinates.
Build this collection from the displayed observation at the presentation
checkpoint, never from final state or undiscovered content. No new rendering
implementation or game rule is needed. Animated emitter positions would require
explicit field updates; V1 does not implement animated emitters.

## Visibility and approximation

Unknown Cells cannot emit and block propagation. Discovery comes exclusively
from `observedTerrain(frame)` at the displayed checkpoint. Observed walls,
closed Doors, windows and Solid Rock block direct propagation; open Doors pass
light. This is a presentation approximation, independent of gameplay LOS.

Occluders are treated as full-height columns. There is no light over a low wall,
window transmission, miniature shadow, specular response to local sources, or
normal-dependent local shading. Receiver-height attenuation uses a shared low
source height. Texture filtering softens boundaries over roughly one quarter
Cell and can produce a small fringe across a thin wall. Light does not bounce
around corners. These compromises avoid per-emitter dynamic lights and shadow
maps while lighting textured models and existing materials consistently.

## Cost and verification (2026-10-28)

GPU work is one additional filtered texture lookup per participating fragment,
independent of source count, with no additional draw calls or shadow passes.
The RGBA8 field uses four texels per Cell dimension, capped at 256 by 256
(256 KiB). A 50 by 50 board uses 160,000 bytes. Radius is capped at four Cells.
CPU rebuilds visit each source's bounded footprint and trace only near blockers.
Large changed observations can still cause a one-frame CPU hitch.

Measured on this development machine; these are representative samples, not a
hardware-independent frame-rate guarantee:

| Case | Field build | Field bytes |
| --- | ---: | ---: |
| One lava source, 10 by 8, Edge | 1.4 ms | 5,120 |
| 48 lava sources, 18 by 10, Edge | 3.4 ms | 11,520 |
| 2,500 sources, 50 by 50, Node | about 59–63 ms | 160,000 |
| 1,250 sources with half the board Unknown, Node | 42 ms | 160,000 |
| 2,500 sources with a 50-edge dividing wall, Node | 99 ms | 160,000 |

The warmed 48-source Edge scene contained three miniatures, 29,463 triangles
and 22 draw calls in both modes. Three camera turns plus captures measured
renderer submission median/p95 of 1.6/1.8 ms On and 1.2/1.4 ms Off; sampled
camera intervals were about 16.7 ms. This small sample excludes initial shader
compilation. These diagnostics measure CPU submission, **not GPU execution**;
no GPU timer query or modest-hardware device test was performed. Initial shader
and model loading spikes remain visible in uncleared timing samples.

Verification used dedicated fixtures, the isolated 5081 host and the protected
Edge data-only dispatcher. Inspected actual PNGs for one source, adjacent
sources, 48 sources, walls, loaded miniatures, On/Off, partial discovery,
movement with On/Off during playback, renderer switching and restart. Console
reports were empty. Port 5080 was preserved.

Local ignored evidence under `.playwright-cli/browser-qa/`:

- `inspection-LL1BZ4`: single source On/Off and diagnostics.
- `inspection-nqWUMJ`: adjacent sources, loaded miniatures and restart.
- `inspection-LA74Z3`: many sources.
- `inspection-dpH64L`: partial discovery with a second hidden lava source.
- `inspection-4iS49G`: movement/playback, toggling and updated exploration.
- `inspection-MrcMuh`: warmed On/Off timing snapshots.

Automated checks: 101 focused lighting/viewport tests; 640 total client and
dispatcher tests; 1,985 .NET tests; solution build and `git diff --check`.
Tests cover hidden-source exclusion, falloff, walls/Doors/rock, uniform gating,
shader composition, instancing, checkpoint reuse, playback, restart, disposal
and idle-frame behavior, including existing Ghost and Facing regressions.
The full client suite required `PROJECTDELVE_PYTHON` to point to the installed
Python with Pillow because the default Python shim failed; no installation or
global configuration was changed.

