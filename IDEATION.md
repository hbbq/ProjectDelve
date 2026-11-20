# Ideation

Use this file as guidance when generating new ideas for Project Delve.

Project Delve began as a physical-first fantasy/dungeon tactical board game and has grown into a composable game and development playground, with a C#/.NET authoritative engine, a browser-based playtest client, a visual ScenarioDefinition designer, and persistent Expeditions. Preserve physical tabletop play where practical, while explicitly welcoming digital-only experiments when they unlock interesting gameplay.

## Current focus

Generate ideas that help discover what makes Delve interesting to play and what the rules need next.

Interesting directions include:

- **new Unit Types**, including Heroes, enemies, neutral or unusual units with new combinations of existing mechanics;
- new Unit Types that deliberately require **one small new rule or mechanic** when that creates interesting physical gameplay — do not limit ideation to what the engine can already express;
- abilities, behaviors, terrain, board features, objectives, scenario structures, and physical components that create useful new tactical situations;
- scenarios that combine existing mechanics in surprising ways, including multiple Sides, unusual agency/controller combinations, summoning, large Units, doors, rooms, and automated behavior;
- ways to use the level designer and DELVE1 transport to rapidly create, vary, share, and playtest scenarios;
- browser presentation or debug ideas when they help reveal whether gameplay, rules, decisions, or events are understandable;
- ideas that expose missing abstractions by first introducing a concrete gameplay need;
- campaign/content ideas that can compose scenarios without unnecessarily coupling campaign structure to scenario internals;
- physical-table experiments that the reference engine and browser can help validate;
- **new Machines and interactive locations**, including dedicated views for redistributing, buying, selling, upgrading, crafting, recruiting, or configuring Units and objects;
- **subscenarios**, including caves, shops, arcade games, races, chess-like Delve scenarios, logistics, and warfare, with explicit entry, return, failure, and reward consequences;
- ideas that deliberately blur the boundary between playing Delve and changing its code, including discoveries in persistent Expeditions that motivate new mechanics.

The browser and level designer are tools for discovering and testing the game, not the main product goal. Rules, Units, scenarios, objectives, physical components, automated behavior, engine boundaries, testing, and playtest workflow are all valid areas.

## Deliberately seek unexpected combinations

**Actively and explicitly search for surprising interactions between mechanics that were designed for different purposes.** This is a core ideation goal, not an occasional bonus. Project Delve has grown into a composable rules sandbox: an idea can be worthwhile simply because combining two existing systems produces an amusing, emergent, tactically interesting, or revealing situation.

Do not limit exploration to the newest feature or to conventional fantasy dungeon play. Look across the **entire current repository**, including old and new Units, abilities, Standing Orders, Sides/hostility, objectives, terrain, Cell Markers, Resources, Machines, automatic world effects, summons, movement, collisions, and behaviors. Ask: *What happens if these unrelated mechanics meet in the same scenario?* Prefer cross-system combinations that the original feature designers would not have anticipated.

**Make this a recurring, visible part of every ideation pass:**
- Propose at least one concrete **unexpected combination of existing mechanics**, even when the primary ideation topic is something else.
- Prefer combinations of **three or more** independently useful systems when they create a new kind of gameplay, but a particularly good two-system interaction is enough.
- Explain the causal chain: which existing rules interact, what surprising result might emerge, and what tiny scenario would test it.
- Seek interactions with meaningful consequences, including productive synergies, interference, accidental sabotage, unintended incentives, chain reactions, and physically funny outcomes.
- Treat strange-but-legal results as discoveries to investigate, **not bugs to fix automatically**. Distinguish actual rule conflicts from legitimate emergent behavior.
- If a combination is currently impossible, identify the **one smallest missing rule** that would unlock it rather than silently assuming it works.
- Do not require balance, strategic optimality, commercial plausibility, or even conventional good sense. **“It sounds fun to try” is a sufficient reason to propose a small experiment.**

Examples of the *kind of thinking* desired, not a fixed list to repeat:
- A Locomotive follows its Rail steps into a Banana Peel, falls Lying, and stops mid-run; meanwhile Workers are trying to cross the track with Resources.
- Summoning a Unit onto a Banana Peel immediately triggers its arrival effect, potentially producing a freshly summoned but Lying Unit.
- Racing Karts follow shared Routes while Workers cross Asphalt to move Fruit from Fruit Trees into Slot Machines, generating items that feed back into racing, attacks and slips.
- A Wizard's relocation ability might interact with a train, a route-following patrol, or a resource carrier in ways neither mechanic was designed for.

These are **inspiration, not preferred recurring proposals**. Invent new combinations from the current mechanics rather than continually recycling the examples. Surprise is more important than staying within a genre. Keep the idea grounded in real rules and verify assumptions against `CONSTRAINTS.md` before asserting a particular interaction is legal.

## Machines, subscenarios, and the larger game

Actively explore **new Machines and interactive locations** as a recurring ideation direction. An Operate action might perform an immediate effect, open a focused interactive view (for example DelveMart, Trading Post, Upgrade Station, Crafting Station, or Vehicle Workshop), or launch a playable **subscenario**. Do not assume every Machine must fit a simple one-step action.

A subscenario uses the **same Delve Engine and core rules**, even when its Units, terrain, objectives, and genre differ radically. Explore caves, shops, chess-like tactical scenarios, kart racing, resource logistics, factory operation, and warfare. Consider how the parent scenario pauses, what Units and objects enter, what comes back, what happens on defeat, and what rewards or changes carry over. These are idea prompts, **not claims that generic subscenario transitions already exist**. Favor a concrete scenario experiment and the smallest missing rule over a speculative universal framework.

**Occasionally propose something deliberately absurd.** A fantasy Hero might win a warfare game on an arcade Machine and bring a wildly overpowered Tank back to the dungeon; a chess-like Machine might award five random Equipment; a mundane hut might contain an elaborate racing simulation. Such examples illustrate the desired spirit, not a fixed idea list. Do not require balance, genre consistency, commercial plausibility, or conventional good sense. Sometimes the imbalance, logistical consequence, or architectural incompatibility is the interesting part.

Project Delve deliberately blurs the boundary between **playing the game and developing the code**. An ongoing Expedition can reveal boredom, unexpected synergies, inconvenient inventories, or new opportunities; changing the engine and returning to the same journey is itself part of the larger game. Treat the backlog as possible adventures rather than a mandatory roadmap.

**In every ideation pass, consider at least one Machine, interactive location, or subscenario idea**, alongside the existing requirement for an unexpected cross-system combination. These may be the same idea, but need not be. Include an intentionally absurd idea from time to time; do not force one into every batch. For each, distinguish current mechanics from proposed ones, note whether it is physical-friendly or digital-only, and suggest a minimal playable experiment.

## Design principles

Prefer ideas that:

- preserve simple, composable rules and physical play where practical, without treating physical feasibility as a gate for digital-only ideas;
- state clearly whether a proposed mechanic is practical at the table, cumbersome physically, or digital-only;
- make the game more interesting, understandable, testable, or pleasant to play;
- start from a concrete Unit, scenario, objective, component, or gameplay situation rather than a generic abstraction;
- allow new mechanics when a concrete idea needs them, then look for the smallest rule/engine concept that supports that idea cleanly;
- keep ProjectDelve.Engine authoritative for gameplay legality and resolution;
- keep presentation separate from rules;
- respect the separation between Side, controller/agency, Unit Type, and scenario objectives rather than assuming Hero-vs-Monster or exactly two Sides;
- use existing GameState, ScenarioDefinition, DecisionRequest, RulesEvent, provider, JSON, and DELVE1 boundaries where they fit;
- expose real design questions rather than adding infrastructure for hypothetical future needs;
- are small enough to investigate or prototype independently.

Do **not** reject an otherwise good gameplay idea merely because the current engine cannot express it or because it would be digital-only. Identify the missing mechanic explicitly and suggest the smallest experiment needed to learn whether it belongs in Delve.

Avoid ideas whose main value is:

- production web infrastructure;
- accounts, authentication, matchmaking, or multiplayer networking;
- framework churn;
- generic abstraction without a concrete gameplay or playtest need;
- moving rules, pathfinding, LOS, target legality, or automated decision logic into the browser;
- creating generalized effect/rules DSLs in anticipation of mechanics that do not yet exist.

## Existing authoring and playtest capabilities

Treat these as current capabilities rather than future ideas:

- ScenarioDefinition is the concrete initial physical setup boundary.
- Scenarios can be serialized as JSON and transported as DELVE1 strings.
- The browser can load and play a DELVE1 scenario.
- The level designer can build, validate, import, export, and play ScenarioDefinition-based scenarios.
- Sides are arbitrary and controller/agency is separate from Side; scenarios are not inherently limited to two opposing factions.

### Prototyping new automated Units

A new enemy or other eventually-automated Unit Type does **not** need a finished Behavior before it can be proposed, implemented, or playtested.

Use the separation between Side and controller/agency as a prototyping tool:

- put the new Unit on its intended Side but give its `(UnitType, Side)` group Human control;
- play the Unit manually to test its stats, abilities, movement, interactions, and any new mechanic first;
- observe what choices make the Unit interesting and what tactical priorities emerge;
- only then encode those priorities as Behavior and switch the group to Automated control.

When useful, put Human-controlled and Automated groups of the same Unit Type on different Sides and play them against each other. This can expose where the current Behavior differs from how the Unit is naturally played.

Do not reject or complicate a new Unit idea merely because its automated policy is not known yet. Unit design, rule/mechanic design, and Behavior design can be separate experiments.

Ideas may build on these capabilities, stress them in unusual ways, or reveal where they need to grow.

## Ideation style

Be exploratory. Do not assume every idea should be implemented.

Look at the current repository before proposing ideas so suggestions build on what actually exists. Prefer ideas that create a useful experiment or reveal a design question.

A good idea should explain:

1. What the idea is.
2. Why it might be useful or interesting now.
3. What part of the current game/client/designer it exercises.
4. What new rule or engine mechanic it requires, if any.
5. Any important physical-game or engine-boundary implication.
6. The smallest experiment that could test whether the idea is worthwhile.

Favor a varied set of concrete ideas over a single large roadmap. Mix ideas that recombine existing mechanics with ideas that intentionally push beyond the current rules. Playful, chaotic, genre-breaking, or deliberately absurd ideas are welcome even when they are unbalanced, physically impractical, or likely to provoke new architecture problems.
