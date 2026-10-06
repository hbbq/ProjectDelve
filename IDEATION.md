# Ideation

Use this file as guidance when generating new ideas for Project Delve.

Project Delve is a physical-first fantasy/dungeon tactical board game with a C#/.NET reference engine and a browser-based visual playtest client. The software exists to make the rules precise and testable while preserving physical tabletop play.

## Current focus

Give extra attention to ideas that make the browser client a better **playtest, visualization, and rules-design tool**.

Interesting directions include:

- ways to make board state, legal choices, activations, and resolved events easier to understand visually;
- presentation and animation ideas that expose whether the engine's rules and event model are clear;
- useful playtest/debug information that can be shown without moving gameplay logic into the client;
- interactions that make the browser version feel natural to play while still using engine-supplied legal candidates;
- ways the client can reveal ambiguities, missing event information, or awkward rules;
- lightweight tools that help create, vary, replay, inspect, or compare playtest situations when there is a concrete need for them;
- ideas prompted by having multiple Heroes, multiple Monster Unit Types, terrain, doors, movement, combat, and automatic Monster behavior on a larger board.
- ideas for new heroes, monsters, bosses or other units
- ideas for new terrain types

The browser is not the only source of ideas. Rules, physical components, Monster behavior, scenario design, engine boundaries, testing, and developer/playtest workflow are all valid areas.

## Design principles

Prefer ideas that:

- preserve the physical-first nature of the game;
- could correspond to rules and state that humans can represent and follow at the table;
- keep ProjectDelve.Engine authoritative for gameplay legality and resolution;
- keep presentation separate from rules;
- use existing GameState, DecisionRequest, RulesEvent, and provider boundaries where they fit;
- expose real design questions rather than adding infrastructure for hypothetical future needs;
- are small enough to investigate or prototype independently;
- make the game more interesting, understandable, testable, or pleasant to play.

Avoid ideas whose main value is:

- production web infrastructure;
- accounts, authentication, matchmaking, or multiplayer networking;
- framework churn;
- generic abstraction without a concrete gameplay or playtest need;
- moving rules, pathfinding, LOS, target legality, or Monster decision logic into the browser;
- adding digital-only mechanics that would not make sense in the physical game.

## Ideation style

Be exploratory. Do not assume every idea should be implemented.

Look at the current repository before proposing ideas so suggestions build on what actually exists. Prefer ideas that create a useful experiment or reveal a design question.

A good idea should explain:

1. What the idea is.
2. Why it might be useful or interesting now.
3. What part of the current game/client it exercises.
4. Any important physical-game or engine-boundary implication.
5. The smallest experiment that could test whether the idea is worthwhile.

Favor a varied set of concrete ideas over a single large roadmap. It is fine to include playful or unusual ideas when they still fit Project Delve's constraints.
