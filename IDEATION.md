# Ideation

Use this file as guidance when generating new ideas for Project Delve.

Project Delve is a physical-first fantasy/dungeon tactical board game with a C#/.NET reference engine, a browser-based visual playtest client, and a visual ScenarioDefinition level designer. The software exists to make the rules precise and testable while preserving physical tabletop play.

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
- physical-table experiments that the reference engine and browser can help validate.

The browser and level designer are tools for discovering and testing the game, not the main product goal. Rules, Units, scenarios, objectives, physical components, automated behavior, engine boundaries, testing, and playtest workflow are all valid areas.

## Design principles

Prefer ideas that:

- preserve the physical-first nature of the game;
- could correspond to rules and state that humans can represent and follow at the table;
- make the game more interesting, understandable, testable, or pleasant to play;
- start from a concrete Unit, scenario, objective, component, or gameplay situation rather than a generic abstraction;
- allow new mechanics when a concrete idea needs them, then look for the smallest rule/engine concept that supports that idea cleanly;
- keep ProjectDelve.Engine authoritative for gameplay legality and resolution;
- keep presentation separate from rules;
- respect the separation between Side, controller/agency, Unit Type, and scenario objectives rather than assuming Hero-vs-Monster or exactly two Sides;
- use existing GameState, ScenarioDefinition, DecisionRequest, RulesEvent, provider, JSON, and DELVE1 boundaries where they fit;
- expose real design questions rather than adding infrastructure for hypothetical future needs;
- are small enough to investigate or prototype independently.

Do **not** reject an otherwise good gameplay idea merely because the current engine cannot express it. If it fits the physical game, call out the missing mechanic explicitly and suggest the smallest experiment needed to learn whether it belongs in Delve.

Avoid ideas whose main value is:

- production web infrastructure;
- accounts, authentication, matchmaking, or multiplayer networking;
- framework churn;
- generic abstraction without a concrete gameplay or playtest need;
- moving rules, pathfinding, LOS, target legality, or automated decision logic into the browser;
- adding digital-only mechanics that would not make sense in the physical game;
- creating generalized effect/rules DSLs in anticipation of mechanics that do not yet exist.

## Existing authoring and playtest capabilities

Treat these as current capabilities rather than future ideas:

- ScenarioDefinition is the concrete initial physical setup boundary.
- Scenarios can be serialized as JSON and transported as DELVE1 strings.
- The browser can load and play a DELVE1 scenario.
- The level designer can build, validate, import, export, and play ScenarioDefinition-based scenarios.
- Sides are arbitrary and controller/agency is separate from Side; scenarios are not inherently limited to two opposing factions.

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

Favor a varied set of concrete ideas over a single large roadmap. Mix ideas that recombine existing mechanics with ideas that intentionally push beyond the current rules. Playful, chaotic, or unusual ideas are welcome when they still fit Project Delve's physical-first constraints.
