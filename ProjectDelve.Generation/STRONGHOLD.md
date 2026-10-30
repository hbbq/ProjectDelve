# Overgrown Stronghold (DNG2-OS)

Overgrown Stronghold generates a small fantasy mission site before realizing its
geography. It uses ordinary scenarios, four canonical Heroes, existing Objectives,
canonical Units and shared Unit Type + Side Standing Orders. It introduces no
runtime encounter script, alarm, sleeping enemy or proximity activation.

## Mission and geography

Three mission graphs supply different spatial problems:

* **Broken Bailey:** a direct route through a ruined gatecourt, an orchard flank,
  and a storehouse approach to the warden's hall. A wayside crypt branches away
  from the mission destination.
* **Twin Halls:** the landing and sanctuary occupy opposite corners. Barracks and
  woodland approaches meet through a central court; a treasury is a side trip.
* **Crown Ring:** the keep lies inside an outer loop, with access from two opposite
  sites. The eastern cloister leads to a separate vault.

The planner reflects these layouts, varies site dimensions and locations, and
inserts up to four roadside redoubts into long connections. Geography then builds
stone roads, gravel trails, grass clearings, tree groves and rock outcrops around
the graph. This is six principal sites, with up to ten including redoubts, rather
than an unlimited hierarchy of generated missions. Large maps remain exploration
maps and take longer to traverse.

Halls and vaults have two adjacent Closed Doors on the front and a two-Cell-wide
breach on the opposite side. Ruins have incomplete walls. Furniture occupies
corners without filling their central maneuvering space. Dense woodland trails
offer screening at the cost of distance; the more direct stone approach is wider
and more exposed. Clustered trees and masonry divide larger lawns into flanks.
There are no new terrain identities in this profile.

Occasional Campfires reuse the canonical Grass-based `campfire` assembly in
groves, courts and ruined encampments. Each site gets at most one, away from the
central 2 × 2 maneuvering square, doorway approaches and reserved road/trail lanes.
The arrival site, destination and enclosed halls/vaults are excluded. A fire needs
four accessible neighboring Cells and at least six passable Cells in its surrounding
ring; fires are at least six Manhattan steps apart. Placement retains connected
floor and then passes the ordinary mission-route checks before Units or treasure
are placed. Campfires block movement, permit LOS and supply only presentation light.

The geography stream tries a fire in 65% of groves and 45% of eligible courts/ruins;
Open Clearings (`O3`) lowers each chance by 15 percentage points. Unsuitable sites
are skipped without forcing a fire elsewhere. The map cap is one plus area/600
(integer division), capped at four. These are placement opportunities, not a
guaranteed frequency: small or heavily traversed sites may have no suitable spot.
Difficulty, defender density and treasure do not choose fire locations.

The Reach Area Objective is the interior of the destination hall or sanctuary,
so merely touching the outside wall does not complete the mission. The existing
all-Heroes / at-least-X-Heroes semantics and opposing Defeat Units Objective are
the same as Heroes vs Monsters. Eliminate Enemies is also available.

## Encounters and treasure

The warden anchors the destination. Difficulty chooses its role and the supporting
roster; pressure controls defender count, with area contributing to the budget.
Lower difficulty uses conventional Goblins, Grunts and Zombies. Higher levels add
Ghosts, Bomb Imps, Trolls, Displacer Demons and occasional Shamans. A Goblin King,
Tower Troll or Red Dragon sometimes provides a different tactical problem. Dragon
placement requires room to maneuver a complete 2 × 2 footprint through open terrain.

Orders apply to the entire Unit Type + Side group, exactly as in authored scenarios:

* Skeleton Archers, the warden's type and Shamans Hold Position. Archers prefer
  positions behind an open breach, with an exterior firing line.
* Goblins and Bomb Imps Protect the warden. They can leave their initial site to
  carry out that Order; they are not dormant local encounters.
* Other defenders use Normal behavior. Their ordinary movement, attacks and
  abilities remain active from the first Round.

At least one archer guards the first optional cache when treasure is enabled.
Redoubts contain sentries to interrupt otherwise long empty approaches. This is
an authored tactical policy, not a claim that every difficulty is equally balanced
for every party strategy. Hold Position does not deactivate attacks or abilities;
a held Shaman still summons, and summoned Goblins inherit their group's Order.

Treasure settings create zero to three off-destination caches, each holding multiple
**unopened Loot markers**. Initial caches never overlap a Unit footprint. They are
not known Items or Equipment and have no preassigned card identity.

The selected Loot preset remains live. At setup, the generator computes:

```text
expected initial defeat drops = ceil(sum(canonical d6 success faces) / 6)
multiplier = max(1, ceil((initial markers + expected drops) / preset card count))
```

Every preset card quantity is multiplied by the same integer, preserving its
relative distribution. A multiplier greater than one is stored explicitly in the
scenario and displayed in the preview. The calculation includes the Heroes and
all initial defenders; it is a supply estimate, not a guarantee for all future
summons. Summoned Units retain canonical drop chances. Gameplay never replenishes
or rescales the finite deck, and the usual marker reservations remain authoritative.

## Controls and recipes

Example complete recipe:

```text
DNG2-OS-30x24-O2-T3-N2-L3-B2-OR2-F1-QClassic-S000000000000002A
```

| Field | Meaning |
| --- | --- |
| Width × height | Each 18–50 Cells; try 18 × 18, 30 × 24 or 42 × 32 |
| O | Openness 1–3: narrow trails, balanced approaches, broad clearings |
| T | Landscape 0 Meadow, 1 Woodland, 2 Craggy woodland, 3 Ember ruins |
| N | Encounter pressure 1–3 |
| L | Difficulty 1–5 |
| B | Optional cache count 0–3 |
| OEE / ORA / OR1–3 | Eliminate Enemies / all initial Heroes reach / at least X reach |
| F | Hero Fog of War: 0 off, 1 on; monsters have no FoW |
| Q | Live Classic, Fun, Experimental or FarOut Loot preset |
| S | Optional 16-digit hexadecimal seed; omitted selects a fresh seed |

Ember ruins may include small Lava features. Generated Lava has
`coalGeneration: false`, preserving its appearance, light and impassability without
producing Coal. Authored Lava retains its default Coal generation unless explicitly
disabled in Designer or scenario data.

Preview is omniscient authoring. Play Unseen generates and enters the normal
authoritative FoW observation without showing a preview first. Existing preview
transfer to Designer, restart and DELVE1 export preserve the concrete setup and
recipe metadata. DELVE1 remains the artifact for preserving a particular layout;
preset references and canonical content remain live.

DNG2-OS has its own SHA-256-derived PCG streams, named
`DNG2:OS:{seed:X16}:{domain}:{attempt}`. The domains are `mission`, `geography` and
`encounters`; no runtime hash ordering or `System.Random` is used. Its first
release is independent of DNG1-HVM. Existing Heroes vs Monsters seed output remains
unchanged. The Campfire integration intentionally retunes DNG2-OS output, so
pre-Campfire Stronghold fingerprints are not retained. Identical seeds and settings
remain deterministic under this policy; use DELVE1 to retain a concrete old setup.

## Quality gates and verification

Generation retries at most 24 deterministic attempts. It rejects overlapping
sites, disconnected passable Cells, inaccessible destinations, short trivial
missions, mandatory single-Cell cuts on a shortest mission route, and missing or
excessively long alternatives around that route's middle. Placement checks complete
footprints, portal clearance, a six-Cell initial approach distance and cache space.
Dragon maneuvering checks treat Closed Doors as blocked. Final scenario creation
uses ordinary engine validation.

These gates measure geography, not combat success or enjoyment. In particular,
the alternative-distance metric is a local detour measurement and does not prove
two completely disjoint routes from every possible tactical position.

`StrongholdGenerationTests` covers deterministic replay and transport, FoW,
separation of Loot from known objects, live preset ratios and an 84-map corpus:
18 × 18, 24 × 20, 30 × 24, 42 × 30, 50 × 50, 18 × 50 and 50 × 18, seeds 0–11,
varying all controls. That corpus contains all three mission graphs, reaches at
most six generation attempts, at most 15 defenders and a maximum ×4 Loot preset.
`StrongholdAccessibilityTests` independently uses the engine's actual placement,
movement and approach queries across 45 additional configurations, including nine
large-footprint placements. It checks mission and treasure accessibility through
the actual emitted terrain and edges, not the planner's connection graph.

## Campfire verification on 2026-10-30

The final solution build had no warnings or errors. Full verification passed
1,917 Engine tests, 494 Web tests (serialized test collections), and 798 JavaScript
and browser-dispatcher tests. Existing Heroes vs Monsters fingerprints are unchanged;
three Stronghold recipes now pin the Campfire policy's new fingerprints.

The Campfire policy was sampled through the generation API across 124 settings/seed
combinations: 40 standard 30 × 24 maps (`O2-T2-N2-L3-B2`, seeds 0–39) plus the
84-map varying-control corpus above (seeds 0–11). All generated and validated
successfully. The standard sample produced 45 fires across 33 of 40 maps (1.13 per
map, range 0–2). The varied sample produced 79 across 60 of 84 maps (0.94 per map,
range 0–3). These are approximate sample frequencies, not guarantees. The compact
18 × 18 sample averaged 0.42 fires; the 42 × 30 sample averaged 1.58.

The corpus now compares generator connectivity with authoritative Engine spatial
queries, checks fire clearance from routes, objectives, Units and Loot, and repeats
each recipe to verify deterministic fingerprints. Independent 2 × 2 accessibility
checks and Heroes vs Monsters fingerprint tests remain in place. Canonical Campfire
content is reused unchanged; neither local definitions nor new effects are emitted.

Representative Campfire recipes inspected in Edge preview, Designer and 3D:

| Recipe suffix after `DNG2-OS-` | Layout and context | Fires |
| --- | --- | ---: |
| `18x18-O3-T1-N3-L1-B1-OR2-F0-QClassic-S0000000000000005` | Broken Bailey, orchard clearing | 1 |
| `30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000000` | Broken Bailey, ruined gatecourt | 1 |
| `30x24-O2-T2-N2-L3-B2-OR2-F0-QClassic-S0000000000000002` | Twin Halls, court and hunter's grove | 2 |
| `42x30-O2-T1-N2-L2-B1-OR2-F0-QClassic-S0000000000000001` | Crown Ring, guarded eastern cloister | 1 |

Desktop/mobile inspections confirmed placement context, generic preview symbols,
Designer properties and warm 3D lighting without failed requests or browser errors.
Some sites intentionally receive no fire because their road lanes or door approaches
leave insufficient space. Fires do not imply sleeping defenders, healing, damage,
production or any other new rule. The samples test placement safety and variety,
not long-term mission balance.

## Original profile verification on 2026-10-29

The full solution build completed without warnings or errors. The broad existing
suite passed 2,135 .NET tests (1,689 Engine and 446 Web) and 758 Node tests. Following
the final narrow-road and Dragon-clearance adjustments, focused verification passed
77 Engine tests, 8 Web tests and 98 affected browser-client tests. The final
Engine.Tests build, including the additional simulation cases below, also completed
without warnings or errors.

Two additional seeded simulation cases ran the engine's ordinary Order-aware
automated provider for four complete Rounds, checking finite Loot reservations at
every boundary. Both use compact 18 × 18 maps with treasure and Ember terrain:

| Seed | Difficulty | Decisions | Living Heroes | Living defenders |
| --- | ---: | ---: | ---: | ---: |
| `0000000000000000` | 1 | 71 | 4 | 3 |
| `000000000000002A` | 3 | 79 | 4 | 4 |

Neither game completed during this short run. This exercises combat and encounter
execution; the automated provider is not a human mission strategy and these two
cases do not establish win rates or balance.

Actual Edge previews covered all three site families and the following recipes:

```text
DNG2-OS-30x24-O2-T3-N2-L3-B2-OR2-F1-QClassic-S000000000000002A
DNG2-OS-18x18-O1-T1-N1-L1-B1-ORA-F0-QFun-S0000000000000000
DNG2-OS-42x30-O2-T2-N3-L5-B3-OR2-F1-QExperimental-S0000000000000001
DNG2-OS-30x24-O3-T0-N2-L2-B2-OR2-F0-QFarOut-S0000000000000005
DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QClassic-S0000000000000002
```

The large high-pressure sample includes a Dragon. The Twin Halls sample exercises
the diagonal mission layout. Inspection found the initial broad roads swallowed
too much screening and the original single Door was bypassable beside its opening;
both were corrected. Final previews have visible stone/gravel approaches, clustered
cover, front Doors with distinct rear breaches and a persistent cache sentry.

Edge checks also covered narrow/mobile layout, direct unseen play with FoW,
Designer transfer and successful Validate, Round Start and a Barbarian Move. There
were no failed requests or console errors (only browser lazy-image information).
Local screenshot evidence is in `.playwright-cli/browser-qa/inspection-3bxogS`,
`inspection-YppX3y` and the earlier gameplay check `inspection-aVDxEf`.

These checks do not replace sustained human playtesting. Three mission graphs and
six principal sites will eventually become recognizable; the planner varies their
geography and encounters but does not invent arbitrary new mission structures.
Difficulty and large-map pacing remain the principal balance limitations.
