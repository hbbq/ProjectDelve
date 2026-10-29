# Scenario variety — idea backlog

Status: **Exploration / parked** (2026-10-29). No implementation requested. This is not a rules specification; do not treat proposals as approved or add them to `CONSTRAINTS.md`.

## Why this exists

After playing *The Bell Beneath Salt*, the five scenarios felt reasonably good but too similar: increasingly difficult hero-party encounters on medium-sized maps. Keep that campaign unchanged for now. Future scenarios should vary **activity, map scale, player role, terrain, information and objectives**, not just enemy counts.

The investigation found that Project Delve already supports production chains, racing, neutral trains, chess-like movement, displacement, Fog of War and more. Often the missing ingredient is an **objective that makes those activities the point of the scenario**.

## Diagnosis of the first experimental campaign

- All five missions use a conventional hero party against automated opposition; maps range from 10×8 to 13×12.
- Combat and summoning dominate; even extraction can become a simple walk after clearing enemies.
- No Fog of War, economic goal, moving finish line or racing victory.
- Scenarios vary tactically, but the player's job and scale remain similar.
- Automated playtests indicate combat-first can dominate some missions; human playtesting remains necessary.

## Existing mechanics worth exploring (verify current implementation before authoring)

- **Different agencies:** control Ghosts, Workers, a Dragon, a Labyrinth Shaper or escorts rather than a conventional hero party.
- **Resources and machines:** Coal, Diamond Press, Geomancer and Troll summoning; resource markers and transport are not general inventories.
- **Racing:** Diamond Karts, boosts, shells, peels and route rewards; demonstration races currently lack a lap-completion victory objective.
- **Rails:** directional rails and neutral Locomotives; collisions and activation order create moving hazards, but there are no passengers or schedules.
- **Movement:** Ghost Phase, chess-style units, 2×2 Dragon, Displacer Demon, Banana Peels.
- **FoW:** permanent exploration; it is not ongoing stealth. Hidden enemies still activate.
- **Standing Orders:** Move To, Protect, Attack, Retreat and Follow Route; Worker behavior has its own priorities, and route progress is group-scoped.
- **World Cards and Labyrinth Shaper:** unstable world effects and changes to walls/doors.
- **Terrain:** visuals alone do not change gameplay; meaningful blocking and LOS properties do.

## Ten scenario concepts

These are **unimplemented concepts**, not tested scenarios. “Playable today” means the investigated engine capabilities appear to support the design, not that the map has been built or balanced.

| Idea | Core activity and choices | Status / caveat |
|---|---|---|
| **The Troll Must Be Manufactured** | Collect Coal, run a Diamond Press, use a Geomancer to summon a Troll, then get that Troll to an inspection bay. Scheduling and space management instead of combat. | Playable today using a Reach Area objective targeting the summoned Troll; start with fixed resources. |
| **The 08:17 Does Not Stop for Heroes** | Cross a rail yard while a neutral train moves by activation; decide when to cross, detour and recover fallen units. | Playable today; no fixed train timetable. |
| **Ghosts at the Municipal Records Office** | Human-controlled Ghosts cross walls in a guarded archive, seeking legal landing spots. | Playable today; make bypassing guards the goal. |
| **Please Do Not Feed the Dragon** | Control one 2×2 Dragon in a broad garden with alternative routes and obstructions. | Playable today at rules level; avoid a single forced corridor. |
| **The Ambassador Is a Pawn** | Escort a route-following Royal Peasant with Knight/Cleric support; intercept threats while the ambassador advances. | Playable today; not actual chess/checkmate. |
| **The Orchard Beyond the Map** | Explore a larger sparse FoW map; choose between splitting up, shortcuts and safe routes. | Playable today, digital-only; discovery must change decisions. |
| **Office Renovation During Office Hours** | Human-controlled Labyrinth Shaper changes walls and doors while evading guards. | Playable today, digital-only; keep initial layout compact/readable. |
| **Diamonds Are a Warehouse's Best Friend** | Competing workshops produce and deliver Diamonds to separate depots; throughput, space and access matter. | Requires **Resources in Area** objective; avoid deadlocked chokepoints. |
| **The Fruit-Powered Grand Prix** | Karts race while pit crews produce/deliver supplies; logistics influences the racers. | Requires **Complete Route** objective; automated Karts may spend items eagerly. |
| **The Last Comfortable Chair** | Tiny positional contest: Displacer Demons try to hold a seating area using displacement and formation. | Requires **Hold Area** objective; beware activation-order sensitivity. |

Potential narrative framing: a disastrous municipal opening day tying production, rail crossing, escort, renovation and racing together. This is only an optional idea, not a committed campaign.

## Candidate generic objectives — NOT decided rules

1. **Resources in Area:** win when a specified quantity of a specified loose resource is physically present in a rectangle. Could enable deliveries, salvage, stockpiling and manufacturing. Open questions: shared-area credit and whether resources can leave afterward. Prefer current visible markers to a hidden historical-delivery counter.
2. **Hold Area:** earn progress at a round boundary by controlling an area. Could enable territorial contests and defence. Open questions: definition of control, Lying units, accumulation versus reset, ties and physical counters.
3. **Complete Route:** designated participant completes ordered checkpoints/route. Could enable races and inspection circuits. Avoid confusing group-scoped route Orders with individual completion; need an observable finish record.

Defer generic triggers, persistent campaign progression, crafting trees, scripted weather and programmable AI until a real scenario demonstrates their value.

## Suggested experiments when interest returns

1. Build and playtest **The Troll Must Be Manufactured** using existing mechanics. Does scheduling create decisions or just repetitive hauling?
2. Build and playtest **The 08:17 Does Not Stop for Heroes**. Does uncertain train activation create recoverable dilemmas rather than arbitrary deaths?
3. Consider **Resources in Area** only if logistics gameplay proves interesting; agree on semantics before implementing.

For later campaigns, alternate **activity, scale and agency**: tiny positional puzzle, industrial task, moving hazard, FoW expedition, then combat. Human testing determines whether the resulting decisions are fun.

## Provenance

Summarized from Astra Ultra's read-only scenario-variety investigation of 2026-10-29 and subsequent design discussion. The investigation made no code or scenario changes. Re-check engine and catalog capabilities before acting on any idea.
