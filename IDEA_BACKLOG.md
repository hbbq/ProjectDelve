# Project Delve — Idea Backlog

Exploratory ideas collected from an independent cross-model ideation exercise (Gemini, Grok, Claude; October 2026), curated for later experiments.

**Status: IDEAS ONLY.** This is not authoritative rules text or an implementation plan. `CONSTRAINTS.md` remains the source of truth. `IDEATION.md` guides future exploration. Do not implement or silently promote anything here to a rule without discussing the concrete design first.

## Why these ideas are here

Prefer small, physically representable rules that interact with unrelated existing systems. A good experiment may be funny, unbalanced, strategically odd, or reveal a missing abstraction. Keep the initial prototype smaller than the imagined full game. The source models sometimes assumed capabilities that **do not exist**; treat all feasibility claims as provisional until checked against current code and rules.

Legend: **No new rule candidate** = intended to use existing mechanics, still requires verification; **Small** = a focused new ability/trigger/order; **Moderate+** = requires substantial resolution/state decisions.

## Best immediate experiments

### Zombie Bucket Brigade — Claude
**No new rule candidate; high priority.** Zombies with Transport Resource follow a shared Loop Route between Lava and a Diamond Press. A resource-carrying Unit reaching a cell with another Resource leaves its cargo, potentially creating a crude conveyor / cargo-swap network. Shared group waypoint progress may make stragglers cut corners.

**Why:** A logistics system emerges from route-following and cell occupancy, with no purpose-built conveyor. Zombie combat and Banana Peels can disrupt the belt.

**Test:** Small map, one Lava, one public Diamond Press, three Zombies on a four-waypoint loop and two Workers. Compare production over ten rounds with and without the loop; seed a resource on the loop to see whether it changes unloading. **Verify** exact carry/drop behavior, route choices and whether Workers can access the cargo; do not assume the system is productive.

### Piñata — Claude
**Small new trigger; high priority.** A Unit produces a random Resource when it *becomes Lying* (perhaps the existing Racing Slot Table). Train collisions, Holy Wave, Banana Peels, Telekinesis and World Effects become possible harvesting tools.

**Why:** Turns posture changes into an economy and gives nonlethal disruption a new purpose.

**Test:** One neutral Piñata Donkey, one Kart, one Cleric and a Worker. Count actual posture transitions and drops. Decide whether spawning already Lying counts, whether repeated transitions can award again, and what happens when the cell already holds a Resource.

### Magnetic Crane — Gemini
**Small–moderate new Machine operation.** An Operate-capable Unit uses a fixed crane to reposition a Unit or loose Resource within a limited range, using existing placement and forced-movement rules where applicable.

**Why:** Connects factory work, rescue, combat displacement, railways and hazard placement.

**Test:** A crane beside a wall or moat, two Workers and one hazard. First implement one target category (preferably Units *or* Resources), not an unrestricted “any object” action. Define LOS, target legality, destination legality, ownership, costs, and whether Cell Markers can ever be moved.

### Curling Coal / Kick Resource — Claude, inspired by Pengo
**Small–moderate new Action.** Kick an orthogonally adjacent loose Resource in a straight line until blocked. Possible extensions: depositing into a Machine or knocking a Unit Lying.

**Why:** Changes logistics from path planning into a geometry puzzle; Resource tokens become projectiles.

**Test:** One kicker, one Coal, a corridor and a Diamond Press. First decide the stopping cell and interactions with walls, doors, Units, Machines, other Resources and Banana Peels. Treat automatic machine deposits and knockdowns as *additional rules*, not already supported behavior.

### Swap Posture — Grok
**Small new Bonus Action.** Exchange Upright/Lying posture between adjacent Units, subject to explicit legality.

**Why:** Creates a tactical posture economy without changing HP or movement rules.

**Test:** Two Units, a Banana Peel and a timed activation. Define whether both postures must differ, whether hostile targets are legal, and how an ongoing activation reacts to becoming Lying.

## Orders, indirect control and coordination

### Echo Order — Grok
**Small–moderate new Standing Order.** During a Unit Type + Side group activation, later Units try to copy the first Unit's movement choice.

**Why:** Turns sequential group activation into coordinated (or comically uncoordinated) movement.

**Test:** Six Workers in a branching corridor. Decide *what* is copied (relative displacement, direction, target cell or action), how blocked moves resolve, whether Actions are also copied, and how it coexists with Follow Route. Copying decisions is new group state, not existing Standing Order behavior.

### Mirror Order — Claude
**Small–moderate new Standing Order.** Group targets a cell mirrored from a referenced Unit across a chosen board axis; recompute when choosing a Move.

**Why:** Existing Units become puzzle pieces without changing their stats. Displacing the referenced Unit changes an entire group's target.

**Test:** Symmetric board with asymmetric obstacles, one Hero and four Zombies. Specify target identity, axis, out-of-board targets, and group progress.

### Indirect-control evacuation / Lemmings — Gemini, Grok, earlier ideation
**Scenario-first candidate.** Automated Units pursue a goal while humans modify Doors, Walls, hazards or positions to help or hinder them.

**Why:** Player agency is environmental rather than direct control. Start with existing Orders and Reach Area rather than adding facing/forward-motion rules.

**Test:** Small corridor with several automatic Units and two human-controlled helpers; only add new guidance markers if ordinary tools prove insufficient.

### Legs & Hands — Claude, inspired by Magic Maze / Space Alert
**No new gameplay rule, but moderate controller/client work.** Split a Side's decisions: one human chooses Unit selection and Moves, another chooses Actions, Bonus Actions, Free Actions and targets; optionally restrict communication.

**Why:** Makes coordination itself the puzzle. A future human-movement / AI-action pairing could test Behavior quality.

**Test:** Short Breakthrough-like scenario with two human players. Current controller assignment is not automatically per-decision-type; verify integration work.

## Environmental and movement experiments

### The Watcher — Claude, inspired by The Sentinel
**Moderate.** Fixed, direction-bearing statue rotates at Round Start and applies a posture effect along a straight LOS beam.

**Why:** Predictable timed hazard, stealth puzzle and new interactions with Lying Units, trains and factories without adding facing to Units.

**Test:** One rotating statue, two Heroes and a Reach Area objective. Decide beam geometry, LOS blocking by figures, initial direction and trigger timing.

### Laser & Mirrors — Gemini, inspired by The Incredible Machine
**Moderate+.** Beam Emitter with movable/rotatable reflectors that redirect an effect.

**Why:** Logistics moves mirrors; mirror placement rewrites hazard geometry and can power or threaten distant machines/Units.

**Test:** One emitter, one reflector and one target. Specify directional ray geometry, reflection, looping beams, LOS, orientation and timing. Standard LOS does not already reflect.

### ChuChu Rocket Arrows — Gemini
**Moderate+.** Directional Cell Markers redirect a Unit or constrain its next movement.

**Why:** Route-followers and Workers can be guided or sabotaged without controlling them directly.

**Test:** One arrow and one automated Unit. Avoid assuming an atomic Move can be redirected mid-path without changing resolution; a *next Move* steering rule may be simpler.

### Gravity Well — Grok
**Small–moderate.** Marker that pulls or pushes a Unit one cell after its Move, using forced-repositioning rules.

**Why:** Emergent movement with Banana Peels, rails, summoning and routes.

**Test:** One Well and one Kart. Prefer affecting only the just-moved Unit initially, not every adjacent Unit after every Move. Specify blocked destinations, corners and chain triggers.

### Fade Marker — Grok
**Moderate.** Marker alternates between blocking and nonblocking each Round.

**Why:** Timing puzzles and routes that periodically open or close.

**Test:** One-cell chokepoint with a timed marker and automated Unit. Requires explicit rules for how markers affect passability/LOS and what happens if a Unit occupies the cell when it closes.

### Slime Cycles — Claude, inspired by Tron
**Small new movement-triggered capability.** After moving, a Unit leaves a Banana Peel (or a new trail marker) behind on its previous cell.

**Why:** Routes progressively obstruct themselves; existing Peel arrival rules make crashes interesting.

**Test:** Two snail Units on a small open board. Define which cell counts as “left” for atomic movement and whether one marker is placed per Move or per traversed cell.

## Bigger, worth keeping

### Passenger / Carrier — Claude
**Moderate+.** Units can board another Unit (Kart, Locomotive, large creature), travel with it and disembark.

**Why:** Reuses vehicles as public transport, rescue platforms and tactical carriers.

**Test:** One Kart bus and one passenger. Resolve physical representation, board occupancy, objectives, collisions, posture, carrier defeat and Activation Bag membership before implementation.

### Katamari Golem Ball — Gemini
**Moderate+ / substantial.** Rolling Unit collects loose Resources, potentially grows, gains strength and loses MOV.

**Why:** Changes Unit scale and board clutter into a snowballing tactical system.

**Test:** Collect only loose Resources at first, with a simple count-based modifier; defer collecting other Units and changing footprint size.

### Metronome Machine — Claude
**Moderate+.** A Machine spends Coal to put an already-drawn group's Activation Token back into the bag.

**Why:** Production becomes time and activation economy, potentially creating runaway feedback.

**Test:** Small bag with two groups and one Machine. Explicitly decide bag-snapshot exceptions, token limits, targets and repeatability.

### Ghost Shift — Grok
**Moderate+.** Temporary state changes traversal and combat eligibility for a Unit Type.

**Why:** Existing combatants become couriers or infiltrators.

**Test:** One ghost-capable Unit and a wall. Avoid claiming walls or enemy occupancy are already traversable.

## Scenario mashups to revisit

- **Sheepdog Trials (Claude):** herd fleeing Shamans into a pen using threat positioning, a moving Kart and displacement; Goblin summons complicate the pen. Scenario-first, but verify Reach Area and Side interactions.
- **Level Crossing (Claude):** exploit Lying posture, railway collisions and bag order. The precise Bomb Imp explosion versus train-position timing is an **open rules question** to investigate, not a decided exploit.
- **Supply-chain Sabotage (Gemini):** attack Worker throughput with Peels and traffic. Do not assume Lying drops carried Resources or Karts have infinite Bananas.
- **Kinetic Sorting Facility (Gemini):** arrange Units around railway crossings and use existing displacement; do not assume the train can scoop cargo or catapult Units.
- **Sports / Delveball (earlier discussion):** Ball Resource, Pass/Shoot/Steal, Dribble/Swap, goal areas, score-and-reset, respawning teams; optionally rail and racing hazards across the pitch. Paused, not decided.
- **Robo Rally (earlier discussion):** committed short programs over future activations; distinct from preference-based Follow Route. Requires blocked-instruction and commitment rules.
- **Bomberman (earlier discussion):** placed delayed bombs and chain reactions; reuse Bomb Imp concepts, but define fuse and blast geometry.
- **Territory / Othello / paint (earlier discussion):** persistent board ownership, conversion and scoring; decide coexistence with existing Cell Markers.

## Explicit cautions from model suggestions

- Machines do not inherently have HP or behave as attackable Units; Machine ownership is not normally mutable.
- Cell Markers are not automatically Terrain and cannot silently overwrite passability or LOS.
- `CantAttack` and stat changes are separate from granting an Attack capability.
- Locomotives do not automatically steal Resources or carry passengers.
- Shared Resource Storage is by Side + Unit Type, not per individual Unit; carrying a loose Resource is different.
- Do not assume involuntary Lying drops a carried Resource, that a Resource can be kicked into storage, or that a World Effect can change arbitrary rules without new definitions.
- Physical-first bookkeeping and authoritative event ordering matter, especially for chain reactions and effects during activation.

## How to resume an idea

1. Check `CONSTRAINTS.md` and the current implementation again; the backlog can become stale.
2. Pick the smallest concrete board and a single interaction to test.
3. Distinguish scenario-only composition from a genuinely new rule.
4. Write down unresolved rule questions before asking Codex to implement.
5. If an idea becomes a decision, move its **rules** to `CONSTRAINTS.md`; keep this file as an inspiration archive.

**Source attribution:** Gemini, Grok and Claude responses to the same Project Delve cross-genre ideation prompt (October 2026). Names and concepts are paraphrased and curated, not endorsed as implemented behavior.
