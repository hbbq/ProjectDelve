# Accessibility Analysis V1

Accessibility is advisory authoring information. It never repairs terrain or
Edges, moves Units, filters Population, changes Loot, consumes RNG, or adds an
acceptance requirement. An isolated optional encounter or a large guard confined
to its chamber remains valid. The existing dungeon access requirement is retained
with a stable root, as described below.

## Engine boundary

`AccessibilityAnalysis.Capture(Board|GameState, revision)` captures a private,
decision-local geometry index. `ProfileFor(unitId)` resolves the instance's
effective movement permissions, persistent native/Equipment grants, footprint,
Phase, pattern, terrain restrictions and movement allowance. `CanStop(profile,
anchor)` checks a complete legal geometric endpoint. `Analyze(profile|unitId,
from, options)` returns ordered stopping anchors, conditional anchors, Door facts,
completion/work counters, revision and explicit assumptions. `StatusAt(cell)`
distinguishes Reachable, Conditional, Disconnected and Unknown. Disconnection is
relative to the selected mode and fixed assumptions, never proof that gameplay
cannot change the situation.

The modes are separate:

| Mode | Meaning |
| --- | --- |
| ActualMove | Captured current Move candidates, including posture, opportunity, figures, explored geometry, markers and effective allowance. Actor queries use the captured position. |
| StaticGeometry | Ordinary cardinal geometric access without activation allowance or special movement-pattern constraints. This is a screening graph, not a legal Move. |
| RepeatedMoves | Closure of legal stopping positions over additional Moves with the supplied fixed allowance and movement pattern. Phase must find a legal endpoint within each Move. |
| ConditionalDoors | Repeated-Move potential plus reachable Door-operation positions and the actual actor's eligible Door abilities. New access is conditional. |

The origin is retained as the current stopping placement even when no Move is
available; this does not grant a Stay action. A profile-only ActualMove query is
single-Move potential without an actor activation claim, explicitly stated in its
assumptions. StaticGeometry excludes figures and special Phase traversal; use
RepeatedMoves for actor-specific passage and legal Phase stops.

The shared Engine `MovementTraversal`, `MovementRules` and `SpatialRules` remain
the movement interpretation. Their optional geometry index replaces lookup cost,
not gameplay rules. Ordinary runtime callers keep their existing pathfinding and
Fog of War behavior. Land and Water permissions are explicit; mixed terrain joins
whichever permissions the profile has. A 2×2 vertex is a complete anchor with
legal internal Edges, terrain, Machine exclusion and boundary clearance. Neutral
Units on another Side block transit exactly like other different-Side Units;
same-Side transit still cannot end in an occupied footprint.

## Assumptions and Doors

Hypothetical analysis is omniscient. Ignoring figures is explicit geographic
potential, not permission to move through guards. Persistent movement grants
remain available while Lying, but a current Move does not. `AssumeUpright` models
an explicitly hypothetical later upright Unit; it neither stands up the real
figure nor grants its next activation a Move.

Repeated analysis freezes the supplied movement profile. Current temporary or
location-dependent stat modifiers do not become a promise that they will persist
in gameplay. Result assumptions describe that limit. The analysis does not plan
combat, behavior decisions, discovery, acquired Equipment, Break, external movement,
cooperation, resource acquisition, Objective completion or mission success.

Door closure opens only Doors bordering a reached, complete legal stopping
footprint. A Phase actor may cross a Door without opening it but must still stop
legally. Open passages, deterministic Open Door, probabilistic Try Open Door,
unsupported operation/cooperation and inaccessible Doors remain distinguishable.
Unlimited supported zero-cost Door actions can extend conditional reach; finite
uses, costs and unsupported timing are not treated as unlimited supply. A
probabilistic opening never becomes an eventual-success guarantee. Chained Door
analysis uses private geometry revisions and never changes the captured board.

## Generator and Quick Play delivery

`WorldPreviewFrame.GeometryAccessibility` appears after normal construction,
route stages and dungeon geometry/Loot finalization, before Population. It reports
open land and Water regions, a principal reference root/area, Site port and dungeon
entrance/room anchor membership, completion and work. These open-region labels
are separate from the historical doors-openable construction-policy region.

Early Forest Enrichment uses a separate decision-local land-component index to
rank natural openings and paths (see [Forest Phase 2](TERRAIN_ENRICHMENT.md#forest-selection-phase-2)).
It is a terrain-authoring policy, not a new acceptance condition in these advisory
audits. Generator formation and construction-policy graphs treat ordinary Closed
Doors as potential passages equivalent to Open Doors; they never change gameplay
Door state. Actor-specific and open-region audits retain their explicitly named
Door assumptions. Forest runs before any construction Edges exist.

`WorldPreviewFrame.UnitAccessibility` appears after Population and before final
publication. Each entry identifies the Unit, encounter and actual start, effective
profile, ordinary/conditional stopping counts, principal membership/access, Door
facts, explicit large dungeon guard intent and assumptions. Earlier frames never
show later Population results. A no-Population world has an empty final instance
audit. The Designer diagnostics show a compact advisory summary; structured
details remain in the generation response.

`WorldQuickPlay.PrepareWithAccessibility` returns a `Preparation` containing the
ordinary temporary scenario and a separate audit of the five actually placed
Heroes. The Quick Play HTTP response adds `accessibility` alongside `transport`.
The audit is full-information authoring data; it is never inserted into gameplay
state or FoW projections. Hero selection, start selection, placement, scenario
fingerprints, DELVE1, saves and replay contain no audit metadata.

## Stable dungeon access

`MountainDungeonInfo.AccessRootIndex` records the top-left Cell of the principal
doors-openable land region selected when that dungeon is planned. Finalization
uses `PlayableWorldComponent.FromRoot` to verify every entrance and usable room
Cell. A blocked root or actual lost connection still fails. Selecting the final
largest component remains Quick Play's independent start policy.

Rock Enrichment's subsequent compatibility phase retains this invariant and
reproduces all six Session B historical failures; see
[Rock phase results](ROCK_ENRICHMENT_PHASE3_RESULTS.md). Changed enrichment can
change the example's area counts below, so regression assertions protect the
smaller retained region, complete dungeon access and rejection of a truly
disconnected root rather than freezing the earlier generated layout.

The regression recipe is:

```
WLD1-50x50-X-17-Y-13-F25-W30-R10-KBroad-HLakes-E1-I2-B1111-C1-NLow-VMedium-P2-Q1-TLow-JMinorPath-L1-ALow-U1-Z2-MMedium-D3-OClassic-S000000000000000D
```

Its original 48-Cell region grows to 165 Cells with the dungeon; another region
grows to 423 Cells. The dungeon remains valid at its original root. Tests also
verify that assigning a truly disconnected root still rejects finalization.

The investigation's accepted island recipe retains fingerprint
`EFF30A2AC22AF0ECDA458D1742C9DFA5D38CBF15838469972E8013605D0BD1C4`.
Its three isolated Bomb Imps each have six geographic stopping positions and no
access to the principal region. This is a diagnostic, not a generation failure.

## Work bounds and verification

Geometry indexes use constant-time terrain/Edge/Machine lookups and live only for
one captured analysis. Safe equivalent ordinary profiles reuse completed regions
within that capture; directed restrictions and special patterns do not silently
reuse symmetric-component conclusions. There is no persistent mutable-GameState
cache and no all-pairs Unit search. Search work is deterministic; exhausting a
budget reports Unknown for unproved destinations.

Generator audits each have a two-million-work ceiling. Unit queries allow at most
100,000 work each, with at most 256 Unit summaries and 1,024 location summaries.
Each Unit retains at most 128 Door details and reports `DoorDetailsOmitted` for
the rest; reachability computation is independent of this display bound.
Special patterns, Phase and Door closure can exhaust their budget. Counts from
incomplete results are lower bounds. Timing is measurement only, never a cutoff
or deterministic diagnostic value.

Focused coverage lives in `AccessibilityAnalysisTests`,
`AccessibilityGenerationTests` and `AccessibilityQuickPlayTests`, alongside
existing movement, dungeon and Quick Play suites.

Measured locally on 12 November 2026, Debug/.NET 10, tiered compilation disabled
for the measurement process only. Each graph sample includes a fresh index and
analysis of a fully tiled open square board; medians use five samples after two
warmups. These fixtures differ from the investigation's generated terrain, so
the figures are not a like-for-like speedup claim. MB are cumulative allocations,
not retained memory.

| Board | 1×1 graph ms / MB | 2×2 graph ms / MB |
| --- | --- | --- |
| 30×30 | 0.68 / 0.41 | 1.88 / 1.64 |
| 50×50 | 1.69 / 1.18 | 5.84 / 4.65 |
| 80×80 | 4.49 / 2.89 | 15.47 / 11.85 |
| 120×120 | 12.67 / 6.35 | 35.48 / 26.58 |

A generated 120×120 Rocky Wilds-style seed-13 world with 192 Units completed
geometry analysis in 34,477 work units. Its separate Unit audit took 1.35 seconds
and allocated 466 MB in one measured run, exhausted the two-million-work ceiling,
and reported incomplete reachability for 51 Units. This is a material V1 limit:
large Phase/pattern/Door analyses can remain Unknown. Allocations include temporary
Engine path and footprint objects; bounded work is not a low-allocation guarantee.
Total generation including both audits took 5.31 seconds in that run.
An open 120×120 board with 192 equivalent ordinary Units is covered separately by
a test requiring a complete audit below 100,000 work through shared region reuse.

Final verification: solution build succeeded with zero warnings/errors; focused
suites passed 211 Engine and 25 Web tests; full suites passed 3,480 Engine and
780 Web tests. Node client/dispatcher verification passed 1,046 tests with zero
failures and one optional benchmark skipped. The first Node run encountered the
environment's Python shim; selecting the already installed Python/Pillow runtime
through `PROJECTDELVE_PYTHON` resolved all nine model-preparation failures.
Session A's initializer was verified before every build/test command, with all
artifacts and dependency caches isolated to Session A. No Session B files or Git
configuration were changed.

## Future policy questions

Repair remains separate work. It needs explicit required relationships (which
party member, Site, encounter or objective), optional/intentional isolation,
Door/cooperation/resource assumptions, allowed geometry changes and crossing
budgets, preservation of Water/large-footprint access, and a failure policy.
Nothing in V1 authorizes excavation, extra crossings, relocating Population or
turning every remote Site into a mandatory destination.
