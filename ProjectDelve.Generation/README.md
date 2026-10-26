# Procedural dungeon generator V1

`DungeonGenerator.Generate(GenerationCode)` produces an ordinary, validated
`ScenarioDefinition`. Generation is setup/authoring; the game engine owns all
movement, combat, AI, exploration, summoning and Objective evaluation.

## Use

Open **Dungeon generator** from the web application's navigation, available in
Designer and Playtest. Configure a dungeon or paste a code and select **Play code
unseen**. No preview is requested or mounted by direct play. The game displays
the completed code for sharing, retains it on restart, and provides **Inspect map
(reveals dungeon)**. Inspection requires a second explicit **Generate Preview**
action before showing the map.

**Generate Preview** deliberately reveals the complete initial scenario.
Configuration changes mark a preview outdated without modifying it. **Open in
Designer**, **Play this preview**, and **Export DELVE1** operate on that preview,
even when newer controls differ. Designer labels later edits as modifications of
the original recipe. Designer source metadata is retained in draft recovery;
it never enters gameplay state. DELVE1 preserves the exact concrete scenario and
is preferred for long-term preservation, including edited scenarios.

## DNG1 contract

Example prefix:

```text
DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1
```

Complete example:

```text
DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF
```

Fields are strict and ordered. Tokens are uppercase, dimensions use lowercase
`x`, and hexadecimal seed input accepts either case. Outer whitespace is ignored.
Canonical output has uppercase hexadecimal seeds, exactly 16 digits, and no
redundant decimal zeroes. `OR4` normalizes to `ORA`. Missing seeds are completed
once per generation operation with 64 cryptographically random bits.

| Field | V1 policy |
| --- | --- |
| HVM | Four canonical Heroes vs the curated Monster allowlist |
| Width, height | Each 14–50 Cells; one outer border of Solid Rock |
| R | Exactly 2–24 rooms, subject to BSP capacity |
| C | 0–3 additional distinct room connections; limited by available room pairs |
| D | 0–3: 0%, 25%, 50%, 75% of eligible portals become Closed Doors |
| N | 1–3: approximately 40%, 60%, 80% of non-start rooms occupied |
| L | 1–5: center of the Monster Threat distribution |
| OEE | Existing Eliminate Enemies Objective on both Sides |
| ORA | All four initial Heroes must Reach Area |
| OR1–OR3 | At least that many initial Heroes must Reach Area simultaneously |
| F | 1 enables Hero FoW, 0 disables it; Monster FoW is always disabled |
| S | Optional unsigned 64-bit hexadecimal seed |

The interior can hold at most `floor((width - 2) / 6) * floor((height - 2) / 6)`
BSP leaves. Invalid requests fail with an explanatory error; dimensions and room
counts are never silently altered. A routing failure retries at most 12 complete
layouts with deterministic attempt streams, then fails without changing parameters.

### Randomness and versioning

PCG-XSH-RR 64/32 uses multiplier `6364136223846793005`, increment
`(sequence << 1) | 1`, the reference two-step initialization, and unsigned overflow.
Bounded integers use rejection sampling, and shuffles use descending Fisher–Yates.
The test vector for state 42 / sequence 54 is
`A15C02B7 7B47F409 BA1D3330 83D2F293 BFA4784B CBED606E`.

Each stream hashes UTF-8 `DNG1:{seed:X16}:{domain}:{attempt}` with SHA-256.
Its first eight bytes, read little-endian, initialize state; the next eight
initialize sequence. Domains are `rooms`, `connections`, `doors`, `placement`,
`encounters`, and `composition`. Attempts start at zero. Encounter counts and
Monster type draws use separate streams, so difficulty cannot change encounter
population or placement through bounded-integer rejection sampling. No runtime hash or `System.Random`
sequence participates. Content density/difficulty/Objective changes preserve
geometry and Hero placement for the same seed and geometry configuration.

Output-affecting changes require a new generator version. Five regression seeds
lock DNG1 scenario fingerprints, alongside the PCG reference vector and transport
round trips. The fingerprint is the first 16 uppercase hexadecimal digits of
SHA-256 over UTF-8 `ScenarioDefinitionJson.ToJson(definition)`. This is lightweight
verification, not a promise of permanent historical generator support. DELVE1 is
the concrete preservation artifact; canonical Unit definitions remain authoritative.

## Geometry and profile

BSP recursively splits only where both children can hold their assigned exact
room counts. Each leaf contains a randomly sized rectangular 3–9 Cell room,
with rock margins. Child regions are joined by a multi-source shortest corridor
through rock; existing floors are endpoints, preventing corridor intersections.
Ordinary Walls close incidental contacts between nearby passages. Additional
routes connect distinct room pairs. Closed Doors occupy legal floor-to-floor
room portals; portal Cells are excluded from initial Unit placement.

An independent orthogonal flood reads the final emitted terrain and Edges,
treating Closed Doors as potential passages. Every carved Cell must be reachable
without Phase. It also checks Door endpoints; authoritative game creation then
validates the whole scenario. No repair is needed for accepted layouts.

All four standard Heroes start Upright at full canonical HP, together in a room
with four clear placements. Monsters never start in that room. Reach Area uses
the most distant other room by ordinary potential-passage distance from the
first Hero, and marks that room's rectangle. The Monster Objective is existing
Defeat Units, selecting Side At Start, with count `5 - requiredHeroCount`.

| Monster | Threat |
| --- | ---: |
| Goblin | 1 |
| Grunt, Zombie | 2 |
| Bomb Imp, Skeleton Archer, Ghost | 3 |
| Troll, Shaman | 4 |
| Displacer Demon | 5 |

Threat band weights at distance 0–4 from difficulty are `16, 8, 3, 1, 1`.
Select the band first, then uniformly select an eligible type within it. At most
one initial Shaman is placed. Its ordinary Goblin summon content and automated
agency are declared without changing runtime summoning. Threat is profile data,
not a combat Stat. Occupied room count is the rounded density percentage, with
at least one encounter. Each occupied room draws 1–(density + 1) Monsters,
limited by clear placement capacity. Other rooms remain empty.

Solid Rock is an impassable, LOS-blocking terrain identity using shared terrain
properties. It has temporary purple-grey hatching in Classic and Enhanced 2D,
with no new artwork or interactions. Phase traversal and legal endpoints,
ordinary attack LOS, and FoW match Tree.

Reach Area mission outlines use structured `BrowserObjective.Area` from the
observed presentation, including each playback snapshot. They sit above Unknown
Cells, are independent of the Waypoint toggle, have no input behavior, and never
change exploration or legal decisions.

## Integration and verification

- `POST /api/generation { code }` returns an omniscient authoring result: definition,
  DELVE1 transport, completed code, prefix, fingerprint and diagnostics. It is
  stateless and does not replace the current game.
- The existing `scenario/import` command accepts `generationCode` instead of
  `transport`. It calls the same generator, retains the completed code as host
  metadata, and publishes the normal FoW observation. Existing revision/session
  checks, command idempotency, controller setup and restart behavior apply.
- Generator request sequence checks discard stale preview responses after newer
  requests or configuration changes. Generating a preview never changes a
  Designer draft; transfer is an explicit action using the retained result.

Verification uses `DungeonGenerationTests`, `GenerationIntegrationTests`, extended
Tree/Solid Rock movement and FoW theories, and the browser-client tests. The
deterministic corpus checks count, connectivity, portal clearance, legal placements,
Threat distribution, density, empty rooms, branches, dead ends, loops, destination
distance, exploration and attempts. A further 128 seeds vary dimensions and room
counts, plus edge-size configurations up to 50 × 50 / 24 rooms.

In the 24-seed 30 × 20 corpus, Sparse/Normal/Dense produce 106/191/360 Monsters and
72/96/144 occupied rooms out of 168 non-start rooms each. Mean Threat by difficulty
is 1.62/2.24/3.04/3.75/4.30. Tree layouts have 73 leaf rooms and 24 branch rooms;
mean destination distance is 36.6 steps and initially explored fraction is 6.7%.
All 216 samples in that comparison succeeded on attempt one. These are broad
distribution checks, not individual-seed balance guarantees.

V1 intentionally has one profile, no combat-balance guarantee, no campaign/runtime
expansion machinery, and no historical compatibility framework. Dedicated Solid
Rock artwork and playtest-driven balance/layout tuning are future work; output
tuning must use a new version. There are no unresolved gameplay design decisions.
