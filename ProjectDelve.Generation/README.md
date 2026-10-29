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
is preferred for long-term preservation, including edited scenarios. Its Loot preset
references and central card rules remain live; use Copy to Custom in Designer to
preserve independent card quantities.

## DNG1 contract

Example prefix:

```text
DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1
```

Complete example:

```text
DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF
```

The Loot Deck selector offers Classic (default), Fun, Experimental and Far Out.
Generated definitions retain a live preset ID and use canonical Unit Type drop rates;
they do not copy deck contents or create drop overrides. Preset choice does not change
the generated layout or population. Old DNG1 recipes without a Loot field use Classic.
DELVE1 files made before presets retain their original no-Loot configuration on import.

Fields are strict and ordered. Tokens are uppercase except the named Loot preset, dimensions use lowercase
`x`, and hexadecimal seed input accepts either case. Outer whitespace is ignored.
Canonical output has uppercase hexadecimal seeds, exactly 16 digits, and no
redundant decimal zeroes. `OR4` normalizes to `ORA`. Missing seeds are completed
once per generation operation with 64 cryptographically random bits.

| Field | V1 policy |
| --- | --- |
| HVM | Four canonical Heroes vs the curated Monster allowlist |
| Width, height | Each 14–50 Cells; one outer border of Solid Rock |
| R | Exactly 2–24 rooms, subject to BSP capacity |
| E | Excavation: 1 Compact, 2 Balanced (default), 3 Spacious |
| C | 0–3 additional distinct room connections; limited by available room pairs |
| D | 0–3: 0%, 25%, 50%, 75% of eligible portals become Closed Doors |
| N | 1–3: approximately 40%, 60%, 80% of non-start rooms occupied |
| L | 1–5: center of the Monster Threat distribution |
| OEE | Existing Eliminate Enemies Objective on both Sides |
| ORA | All four initial Heroes must Reach Area |
| OR1–OR3 | At least that many initial Heroes must Reach Area simultaneously |
| F | 1 enables Hero FoW, 0 disables it; Monster FoW is always disabled |
| Q | Optional live Loot preset after F, before S: `QClassic`, `QFun`, `QExperimental`, `QFarOut`; Classic is omitted in canonical output |
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

**DNG1 is under active development.** Its current implementation is deterministic:
the same complete configuration and seed produce the same ScenarioDefinition in
Preview, Designer transfer and Play Unseen. Development refinements may change
what an earlier DNG1 code generates. There is no historical algorithm, migration
layer or DNG2. Current regression fingerprints are updated with intentional tuning;
they check the current implementation, not permanent historical output.

Use **DELVE1** to preserve a good dungeon exactly, including for campaigns.
It stores the concrete ScenarioDefinition rather than a generator recipe.
The fingerprint is the first 16 uppercase hexadecimal digits of
SHA-256 over UTF-8 `ScenarioDefinitionJson.ToJson(definition)`. This is lightweight
verification, not a promise of permanent historical generator support. DELVE1 is
the concrete preservation artifact; canonical Unit definitions remain authoritative.

## Geometry and profile

BSP recursively splits only where both children can hold their assigned exact
room counts. Each leaf retains a rock margin and contains a rectangular room.
Compact draws each dimension from 3–9 Cells, bounded by its partition. Balanced
draws from 65–100% of the available interior extent; Spacious from 85–100%.
Larger rooms remain distinct within their partitions rather than arbitrarily
painting rock into floor.

All rooms are placed before child networks are joined. A deterministic multi-source
weighted route through rock favors space for two-Cell width. Existing floors are
endpoints, and free rock across BSP separators is available for widening.
Routed spines are widened into available rock,
using the other side when the preferred side is obstructed. Requested widths
draw from 10% one Cell, 70% two Cells and 20% three Cells; a narrow draw on a
route longer than six spine positions becomes two Cells. Extra connections prefer
room pairs with space for wide routes, retaining narrow fallback connections when
needed to honor the configuration. Local geometry can retain
narrow throats and Doorways. Only the original routed portals connect into
existing floors; ordinary Walls seal lateral contacts, preventing widened
corridors from bypassing a Closed Door or connecting an unintended room. Additional
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
counts across all three Excavations (384 configurations), plus edge-size checks
up to 50 × 50 / 24 rooms.

The 24-seed 30 × 20 excavation comparison produces these means (seeds 0–23):

| Excavation | Passable | Solid Rock | Room area | Corridor Cells/map | Initial explored | Entirely hidden rooms/map |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Compact | 41.31% | 58.69% | 18.5 | 99.9 | 6.51% | 6.67 |
| Balanced | 47.87% | 52.13% | 26.8 | 72.9 | 7.80% | 6.71 |
| Spacious | 52.67% | 47.33% | 33.5 | 48.4 | 8.61% | 6.83 |

All 72 layouts succeed on attempt one, with zero disconnected floors or invalid
Doors/placements. The independent varied-size corpus covers all three settings.
Measured corridor spine cross-sections (including narrow room approaches) are
6.3/59.6/34.1% one/two/three-or-more Cells for Compact,
6.0/68.3/25.7% for Balanced and 8.3/64.4/27.3% for Spacious.
Requested widths across all 648 connections are 53/452/143 one/two/three Cells.
This distinguishes routing policy from actual usable width: Spacious rooms leave
short, narrow connections at their boundaries. Two Cells is the majority
measured width in every setting; narrow Door portals remain useful chokepoints.
These are corpus measurements, not individual-seed balance guarantees.

V1 intentionally has one profile, no combat-balance guarantee, no campaign/runtime
expansion machinery, and no historical compatibility framework. Dedicated Solid
Rock artwork and playtest-driven balance/layout tuning are future work; output
tuning continues within DNG1. The approved closed-corner LOS rule is implemented
in the shared engine geometry; see the follow-up in [VERIFICATION.md](VERIFICATION.md).
Generator tuning does not define those gameplay rules.
