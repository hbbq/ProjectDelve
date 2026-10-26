# Dungeon refinement and Fog of War verification

Closed-corner follow-up: commit `481892d8fb6017f06247db72728b93bbc7777c2d`
approved the shared rule, which is now implemented in `AttackRules.cs`.
The Wizard at `(13,7)` no longer discovers `(11,5)` or `(10,4)` with fresh
exploration; legitimate earlier exploration remains permanent. Verification:
1,601 Engine tests, 374 Web tests and 420 client/security tests passed.
Edge confirmed Unknown Cells in both views and a visible Shaman when the Wall
was removed as a control, with no console/request errors. Captures are under
`.playwright-cli/browser-qa/inspection-6ayYaL/` (`wizard-closed-enhanced.png`,
`wizard-closed-classic.png`, `wizard-open-control.png`). The investigation and
generator measurements below record the earlier baseline; their LOS deferral
and permissive corner results are superseded by this follow-up.

Verified 2026-10-26. **LOS semantics were left unchanged at the user's request,
for a follow-up rules discussion.** `CONSTRAINTS.md` and production catalog
scenarios were not modified. Animation and generator improvements are implemented.

## Wizard/Shaman finding and examples

The supplied DELVE1 is retained in
`ProjectDelve.Engine.Tests/Fixtures/wizard-corner.delve`. `WizardCornerTests`
also contains a small independent corner fixture.

Neither `(11,5)` nor `(10,4)` is initially explored in the exact scenario.
Reconstructing a fresh authoritative state with the Wizard at `(13,7)`, clearing
exploration and recalculating it, discovers both. The Wizard has effective RNG 4,
hence sight range 7. The targets' Manhattan distances are 4 and 6. All three other
Heroes have blocked LOS from their original positions. No Phase, prior permanent
exploration or swept sight is needed to reproduce this observation. The actual
historical movement choices were not supplied; this reconstruction isolates
current LOS without inventing that history.

Both rays lie on `x - y = 6`, crossing corners `(12.5,6.5)` and `(11.5,5.5)`.
At the latter corner:

```text
           x=11          x=12
y=5        Floor         Solid Rock
           south Wall   [Wall ends at corner (11.5,5.5)]
y=6        Floor         Floor <-- ray approaches through here
```

The south Wall of `(11,5)` closes the passage via `(11,6)`. The alternative only
touches the corner of rock `(12,5)`. The implementation tests blocking edge
passages and terrain interior intersections separately, so that corner touch does
not close the alternative. The earlier corner also only touches rock `(12,7)`.
Ordinary attack LOS permits the same ray. This is not exploration propagation,
another Hero's vision, or a Solid Rock identity defect.

The constraints explicitly exempt terrain corner touches, while also saying
all passages through a corner must be blocked to stop LOS. How mixed terrain
and edge closures should interact needs a ruling. The user requested findings
and examples for a follow-up instead of changing these semantics here.

**Two diagonally adjacent Solid Rock Cells show the same effect:**

```text
           x=1       x=2       x=3
y=1        Wizard    Rock      Floor
y=2        Rock      Floor     Floor
y=3        Floor     Floor     Shaman
```

LOS from `(1,1)` to `(2,2)` or `(3,3)` passes between the touching corners of
rocks `(2,1)` and `(1,2)`. Initial FoW reveals the diagonal Cells and Shaman.
Trees behave identically. A horizontal ray through a rock's interior is blocked.
Two Walls closing both corner passages block normal LOS. Phase exploration
ignores those Walls; ordinary attack LOS still respects them.

Regression checks cover the reported Cells, neighbors, blocking destinations
versus terrain behind them, Trees, Phase, initial versus swept sight, permanent
exploration, and another independently placed Hero. An explored Cell is tested
not to become a vision source. The follow-up should decide whether terrain
touches close passages when paired with a Wall, another blocking terrain Cell,
or both, and settle the shared normal LOS/FoW rule explicitly.

## Movement presentation

Previously only an already rendered token could animate. Entering Units popped
into the final snapshot; leaving Units stopped at the last retained known Cell;
disconnected known runs were reduced to a notice.

Observation now retains contiguous observed runs with at most one real adjacent
boundary step at either end. Hidden interiors remain omitted. Separate runs do
not become a straight line across hidden space, and leaps receive no invented
transit. Narrow appearance metadata supplies an observed mover even when absent
from the final state. Narration and ordinary paths remain filtered.

Both renderers use a temporary non-interactive token beneath opaque fog masks.
Each run starts at its actual first position without a transition, then animates
its steps. Cards and stable state are not advanced to the destination beforehand.
Unknown-to-Unknown events remain omitted. Final authoritative snapshots settle
the board after normal, disabled or skipped playback; cancellation removes
temporary tokens/masks. No movement, activation, Phase or attack rule changed.

Node tests execute actual playback for known/known, entering, leaving,
disconnected known runs, Doors and Phase in Classic and Enhanced, with animated,
disabled and skipped playback. Projection tests cover completely hidden movement,
event-time history and idempotent observation. The dedicated `fog-patrol.json`
drives an authoritative two-round regression and live Edge timing acceptance.

## Excavation, widths and determinism

The new canonical prefixes and complete codes include Excavation after rooms:

```text
DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1
DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF
```

E1 is Compact, E2 Balanced/default, E3 Spacious. Optional seeds still complete
once. Parsing is strict and ordered; missing/out-of-range Excavation fails.
There is no DNG2, historical algorithm or migration shim. Current complete codes
reproduce exact current ScenarioDefinitions. DELVE1 preserves a concrete dungeon
across future DNG1 development.

Rooms grow structurally inside BSP leaves. All rooms precede routing. Weighted
routing favors two-Cell space, and widening can use free rock across partitions.
Existing floors remain protected except at routed endpoints. Walls seal lateral
contacts, including beside Doors. Extra loops prefer room pairs with space for
wide routes, retaining a narrow fallback for tight configurations. Random narrow
draws apply only to short routes.

Deterministic seeds 0–23 at 30 × 20 / eight rooms give these means:

| Setting | Passable | Rock | Room area | Corridor Cells/map | Initial FoW | Hidden rooms/map |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Compact | 41.31% | 58.69% | 18.5 | 99.9 | 6.51% | 6.67 |
| Balanced | 47.87% | 52.13% | 26.8 | 72.9 | 7.80% | 6.71 |
| Spacious | 52.67% | 47.33% | 33.5 | 48.4 | 8.61% | 6.83 |

All 72 layouts succeed on attempt one: zero retries/failures, disconnected floors,
illegal Door endpoints or invalid placements. Larger rooms reduce separate
corridor area while increasing playable area. These are geometry measurements,
not combat-balance guarantees.

Across 648 connections, requested widths are 53 one-Cell, 452 two-Cell and 143
three-Cell. Actual spine cross-sections include turns and narrow approaches:

| Setting | One Cell | Two Cells | Three or more |
| --- | ---: | ---: | ---: |
| Compact | 6.3% | 59.6% | 34.1% |
| Balanced | 6.0% | 68.3% | 25.7% |
| Spacious | 8.3% | 64.4% | 27.3% |

Tests require a two-Cell majority and under 15% one-Cell sections in each corpus.
Interior measurements excluding first/last approach Cells are Compact 43/404/281,
Balanced 21/276/132, Spacious 2/91/61 for widths 1/2/3+. Doorways remain single-Cell
portals; every other lateral room contact must be walled off. Configured loops
may legitimately provide other routes through the dungeon.
In the captured Balanced `S0123456789ABCDEF` layout, `(6,10)` lies on a two-Cell
cross-section of a vertical corridor and `(22,8)` on a three-Cell cross-section
of a horizontal corridor. Both are visible in the Balanced preview capture.

Existing density/Threat/topology, Objectives, DELVE1 transport and current
fingerprint checks remain. The varied-size corpus covers all Excavations:
384 configurations, plus boundary checks through 50 × 50 / 24 rooms.

## Automated verification

```text
dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli
dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli
<installed Node> --test ProjectDelve.Web.Tests/*.test.cjs tools/test-browser-qa.cjs
```

Build: zero warnings/errors. Full .NET: **1,561 Engine + 374 Web = 1,935 passed**.
Node: **420 passed**, including dispatcher security checks. No
unrelated tests were weakened or production scenarios changed for tests.

## Edge acceptance and evidence

The isolated host used port 5081; the existing 5080 process was preserved.
Standard inspection used the unchanged protected `.codex/browser-qa.ps1`.
`ProjectDelve.Web.Tests/fog-movement.browser.cjs` received separate execution
review required by `BROWSER_INSPECTION.md`: fresh headed Edge, temporary context,
fixed localhost origin, external requests blocked and browser closed in finally.
The dispatcher has no timing assertions, which necessitated that separate check.

Live acceptance passed **16 checks**: entering/leaving across Floor, an Open
Door and Phase through Solid Rock/a Closed Door, plus disabled and skipped
playback in both renderers. Ordinary runs show 30–31 frames over 483–500 ms.
Phase runs show 15–16 over 233–246 ms: the Ghost's canonical MOV 2 gives a shorter
observed run. Disabled playback produces zero frames; Skip settles after four
sampled frames. Assertions check position changes, opaque masks above tokens,
non-interactive temporary figures, final observability, cleanup and omission of
hidden endpoints from narration. Successful acceptance and final dispatcher
plans reported no console errors or failed requests.

All paths below are relative to the repository and ignored. PNGs were opened
and visually inspected.

| Evidence | Path |
| --- | --- |
| Wizard (13,7), current corner sight | `.playwright-cli/browser-qa/inspection-PrU0U0/wizard-current-corner.png` |
| Same Wizard position, Classic | `.playwright-cli/browser-qa/inspection-PrU0U0/wizard-current-classic.png` |
| Two diagonal rocks | `.playwright-cli/browser-qa/inspection-PrU0U0/diagonal-rock-current.png` |
| Compact, Balanced, Spacious | `.playwright-cli/browser-qa/inspection-p8oMdf/compact.png`, `balanced.png`, `spacious.png` |
| Classic/narrow generator | `.playwright-cli/browser-qa/inspection-p8oMdf/spacious-classic.png`, `spacious-narrow.png` |
| Emerging/leaving Monster | `.playwright-cli/fog-acceptance-2okMzU/enhanced-plain-enter.png`, `enhanced-plain-leave.png` |
| Classic movement | `.playwright-cli/fog-acceptance-2okMzU/classic-plain-enter.png`, `classic-plain-leave.png` |
| Door/Phase movement | `.playwright-cli/fog-acceptance-2okMzU/{classic,enhanced}-{door,phase}-{enter,leave}.png` |
| Timing evidence | `.playwright-cli/fog-acceptance-2okMzU/report.json` |
| Unseen Enhanced/Classic/narrow | `.playwright-cli/browser-qa/inspection-gtkvLi/direct-unseen-enhanced.png`, `direct-unseen-classic.png`, `unseen-narrow.png` |
| Restart code | `.playwright-cli/browser-qa/inspection-gtkvLi/restart-code.yaml` |
| Same-code preview/Designer | `.playwright-cli/browser-qa/inspection-gtkvLi/same-code-preview.png`, `designer-transfer.png` |

Initial attempts caught unsupported selectors, asynchronous preview captures and
queued details-toggle timing in the acceptance driver. These were corrected
without modifying the dispatcher or execution rules. A build while the QA host
held its DLL was successfully retried after stopping only that host session.

## Remaining scope

The shared corner-LOS ruling is intentionally deferred. The Wizard sightline
has not been blocked. Combat balance, final Solid Rock artwork, additional FoW
modes and historical generator infrastructure remain outside this task.
