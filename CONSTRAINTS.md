# Project Delve — Constraints

This document captures the foundational constraints for Project Delve. It is intentionally focused on the physical game and its representation, not on gameplay rules that have not yet been decided.

The digital implementation is a reference implementation of a physical board game. The physical game is the source of truth for what must be possible.

## Design principles

### Physical first

Every mechanic must be possible to perform and represent in the physical game.

The digital implementation may automate rules and calculations, but it must not introduce capabilities that only make sense because a computer is available. Randomness, state changes, positioning, and other mechanics must have a plausible physical equivalent.

The digital representation models the physical game. The physical game must not need to adapt to shortcuts or limitations in the digital implementation.

### Explicit rules

Implementation details must not silently become game rules.

If the software needs to know how a situation is resolved, the corresponding rule should be made explicit. Ambiguities discovered while implementing the game are opportunities to clarify or revise the game design.

### Physical State is a first-class model

Physical State describes the observable physical situation on the table without interpreting what it means according to the rules.

It includes, at minimum:

- the board layout,
- the terrain tile at each grid position,
- physical features on edges,
- physical state of changeable features such as doors,
- which figures are on the board,
- the grid position of each figure,
- the physical posture of each figure.

Physical State is separate from Rules/Game State.

### Rules/Game State is separate

Rules/Game State contains information required by the game rules that is not simply the physical layout of the board.

Examples may eventually include health, actions, abilities, experience, activation state, and turn state.

Exactly what belongs here will be defined as the rules develop.

### Presentation is separate

Physical State and Rules/Game State must not depend on a particular renderer.

The same state should in principle be representable as, for example:

- a simple textual or ASCII representation,
- a 2D graphical representation,
- a 3D representation,
- the actual physical board and components.

## Reference implementation architecture

The initial reference implementation uses C#/.NET.

The architecture exists to support the physical game rules rather than to define them. Game rules must remain independent of transport, UI, persistence technology, and rendering.

### Persistent, stepwise game flow

The rules engine is modeled as a persistent, stepwise state machine rather than as a continuously running game loop.

At any stable point, the complete game can be represented by serializable state. The engine accepts a command or decision, applies the relevant rules, advances as far as it can, and returns a new stable state.

The engine may automatically progress through states that require no decision. When external input is required, it stops with enough serialized context to resume the same resolution later.

This allows a game to be saved and resumed and allows the same engine to be driven by different clients, such as tests, a CLI, a web client, or a mobile client.

The rules engine itself must not depend on HTTP, a particular API framework, UI technology, or a continuously connected client. Those are adapters around the engine.

Not every rules operation should become its own state-machine state. Flow states represent meaningful points in game resolution where progression, context, or an external decision must be preserved. Ordinary rules operations remain ordinary operations. For example, a Unit's complete movement path remains one atomic Move operation.

Physical random mechanisms such as Activation Token draws and dice rolls are explicit parts of rules resolution. Their digital random sources should be replaceable or controllable for deterministic tests.

### Decisions, agency, and randomness

All choices that require agency are resolved through a Decision Provider abstraction rather than being hard-coded to a particular kind of player or client.

The rules engine determines when a decision is required and computes the complete legal decision space before consulting a Decision Provider. A Decision Provider only selects from the concrete legal candidates supplied by the engine, or selects no option when the rules permit that choice.

The same decision-space contract is used regardless of who or what has agency over the Unit. A human player, Monster Behavior, test provider, simulation provider, or future smarter AI can therefore receive the same legal candidates and choose among them without changing movement, combat, targeting, or other legality rules.

Examples include:

- which Unit is selected when a rule requires a choice,
- which reachable destination a Unit moves to,
- which legal target it attacks,
- which Action or ability it uses,
- any other rules-defined choice.

For v0 movement, the engine supplies the legal reachable destinations, each with its canonical path. The provider chooses a destination or no movement when permitted.

For Act resolution, the engine supplies the complete legal set of action candidates from the Unit Type's capabilities. Normal Attack candidates apply Range, Line of Sight, hostility, and all other attack-legality rules. Open Door candidates identify each adjacent closed door separately. The provider chooses one candidate or no Action when permitted; either choice completes the Unit's Act phase.

Hero Unit Types have Normal Attack and Open Door by default. Default Monster Unit Types have Normal Attack only. These are composable Unit Type capabilities, independent of side and agency; a particular Monster Unit Type can also have Open Door. Open Door is legal when the Unit occupies either cell bordering a ClosedDoor edge. Resolving it consumes Act, changes that edge to OpenDoor, and emits a DoorOpened gameplay event identifying the Unit and edge. Movement through the opened edge uses the existing OpenDoor movement rules. Default Monster behavior does not choose door opening.

A provider does not establish or extend legal choices. After a provider returns its selection, the engine validates that the selection corresponds to one of the candidates in the decision space that was supplied. An invalid provider response must not become a legal game action.

The engine consults a Decision Provider only when there is a meaningful choice. Exactly one candidate with no option to do nothing is selected automatically. Zero candidates with an option to do nothing resolves automatically as no selection. One candidate plus the option to do nothing remains an external decision, as do multiple candidates. Automatic resolution continues until the next meaningful external decision or a stable terminal state.

Different Decision Provider implementations may supply decisions without changing the rules engine. Expected examples include:

- a human player through a client,
- Monster Behavior,
- deterministic or random providers for tests and simulations,
- a possible future smarter AI provider.

Monster Behavior therefore participates through the same decision boundary as other forms of agency. It defines how Monsters choose among legal alternatives rather than implementing separate movement, combat, targeting, or activation rules.

A decision and a random outcome are different concepts.

Physical randomness such as drawing an Activation Token or rolling Attack and Defence Dice is supplied through a separate Random Provider abstraction. The rules define which physical random mechanism is required and how its result is interpreted; the provider supplies the outcome.

Different Random Providers may support different contexts, for example:

- digital random resolution during normal digital play,
- deterministic predefined outcomes in tests,
- fast automatic resolution during simulations,
- manually entered results from physical dice or other physical random mechanisms.

A human pressing a **Roll** or **Continue** button does not make the random result a decision. Such interaction may control presentation pacing or authorize the engine to continue to the random resolution, while the Random Provider remains responsible for the outcome.

The state-machine and client boundary should therefore distinguish genuine rules decisions from interactions that merely control when an otherwise automatic or random resolution proceeds.

### Engine results and rules events

Each engine run starts from a stable state that is waiting for input, accepts a command or decision, and resolves completely until the next stable point that requires external input or the game ends.

The result of such a run contains, conceptually:

- the new authoritative Game State,
- a sequence of Rules/Domain Events describing what happened during the run,
- the next required input, if any.

Rules/Domain Events describe semantically meaningful gameplay outcomes that have already been resolved by the engine. They are not commands that a client must execute in order to produce the new Game State.

The authoritative state is the Game State itself. v0 does not require event sourcing or reconstruction of Game State from the event history.

Rules time and presentation time are separate. The engine does not wait for a graphical client to finish animations before rules resolution continues. A client may receive a completed engine result and then present its Rules/Domain Events over any suitable amount of real time.

Rules/Domain Events should reflect the semantics of the rules rather than the desired granularity of a particular UI. There is deliberately no required one-to-one mapping between Rules/Domain Events and presentation events or animations.

For example, a complete Move remains one atomic rules operation and may produce one `UnitMoved` event containing the complete path. A graphical client may translate that single event into several per-cell movement animations.

Conversely, several Rules/Domain Events produced while resolving an attack may be combined by a client into one coherent attack animation or presentation sequence.

A presentation adapter or client is responsible for translating Rules/Domain Events into its own presentation events, animation timeline, text output, sounds, or other UI effects. Presentation-specific events do not belong in the core rules engine.

Rules/Domain Events should expose enough meaningful information for different presentations to represent what happened without exposing low-level engine implementation details. Internal operations such as path validation or rule-component lookup are not gameplay events merely because the engine performs them.

### Composable rules and abilities

Unit Types are primarily composed from data, behavior, and reusable rule components rather than implemented as deep class hierarchies that override the game engine.

Abilities and other special rules are initially implemented as C# rule objects.

Core rules provide simple defaults. Abilities and special rules may explicitly modify or override those defaults through narrowly defined rule extension points.

Extension points should be introduced when actual game content requires them rather than by creating a large speculative set of hooks in advance.

Simple Unit Type variation should preferentially be expressed through small reusable **Actions**, **Capabilities**, and **Behaviors**, analogous to keywords on a physical Unit card. An Action defines something the Unit can choose to do. A Capability modifies how an otherwise shared game rule applies to that Unit Type. A Behavior describes how an automated Decision Provider prefers to play that Unit Type among choices that are already legal. These components may carry small parameters when the same mechanic varies between Unit Types.

Actions and Capabilities belong to the game rules and remain true regardless of who controls the Unit. Behaviors belong to automated decision-making: they do not establish legality or modify what the Unit can do, and they have no rules effect when the Unit is controlled by a different provider such as a human player. Rules code should act on reusable Actions or Capabilities, and automated providers should act on reusable Behaviors, rather than special-casing a specific Unit Type identity such as `if Zombie`.

This is a content-composition principle, not a requirement for a generic keyword framework, scripting language, or universal effect system. New reusable components should be introduced from concrete game content as needed.

### Resolution timing and rule extension points

Ordinary rules operations resolve atomically unless a rule explicitly defines otherwise. For example, an Attack completes its complete normal resolution, including dice, Damage, death, and removal, before effects that occur **after** that Attack are resolved.

The engine does not require a general interrupt or trading-card-game-style resolution stack. A rule that occurs after another operation is a subsequent resolution at a defined post-resolution timing point, not an interruption inserted into the middle of the completed operation.

Post-resolution effects may themselves require further rules operations or decisions. For example, an ability could grant an additional Move after an Attack. Such follow-up resolution must be able to preserve enough continuation context for the engine to complete it and then resume the normal activation flow. If external input is required, this context must remain serializable so resolution can stop and resume through the normal persistent state-machine mechanism.

An effect that genuinely needs to participate in or modify an operation while that operation is being resolved should do so through a narrowly defined rule extension point, conceptually an `On(...)` hook for the relevant part of that operation.

This establishes the conceptual distinction:

- `On(X)` participates in or modifies the resolution of `X`.
- `After(X)` occurs only after `X` has fully resolved and begins subsequent resolution.

Concrete `On(...)` extension points and detailed trigger timing are not defined speculatively. They should be added only when an actual ability or rule requires intervention at that point.

Likewise, ordering rules for multiple simultaneous post-resolution effects should be defined when real game content creates that situation rather than by introducing a general-purpose trigger stack in advance.

A rule component should be reusable by different Unit Types when they share the same ability or effect. A Unit Type should not need a bespoke subclass merely to participate in an existing rule effect.

Abilities define capabilities and effects. Monster Behavior remains conceptually separate and determines decisions about legal choices without changing their legality.

Some rule components can resolve immediately, such as a modifier to an effective stat. A rule or ability that requires an external choice must be able to suspend resolution at a stable, serializable flow state and continue after that choice is supplied.

Physical rules text and executable rule behavior are separate concerns. An ability may carry metadata such as its name and physical card text, while its executable behavior is implemented in code.

A custom ability scripting language or DSL is not part of v0. If recurring simple patterns emerge from real abilities, those patterns may later become data-driven while exceptional rules remain code-based.

## Board

The board is an orthogonal grid of square cells.

There is no fixed board size. A typical scenario is expected to be around 15 × 10 cells or smaller, but the model must not impose that as a maximum. Larger boards and different proportions must remain possible.

There are no meaningful positions between cells.

## Terrain tiles

Every grid cell contains exactly one terrain tile.

Each terrain type defines two independent base properties:

- **Passable** — whether a Unit may move through or into the cell, before Unit occupancy and other rules are considered.
- **Blocks LOS** — whether the cell blocks Line of Sight.

Passability describes the terrain itself. Unit occupancy is a separate rule: a passable cell can still be unavailable as a movement destination because another Unit occupies it, and Units may affect whether it can be traversed.

The initial terrain types are:

| Terrain | Passable | Blocks LOS |
| --- | --- | --- |
| Grass | yes | no |
| Tree | no | yes |
| Water | no | no |
| Stone floor | yes | no |
| Stone floor with table | no | no |

There is no separate terrain-level distinction between being passable and being occupiable. Under the base rules, terrain that is passable may also be occupied; whether a particular Unit may actually end movement there is determined separately, including Unit occupancy.

Terrain tiles currently fall into two broad physical categories.

### Flat tiles

A flat tile has no physical geometry preventing a figure from being placed on it.

Examples include:

- grass,
- stone,
- water,
- tiles with printed symbols or special markings.

Being physically able to place a figure on a tile does not imply that the game rules allow the figure to occupy or move onto it.

### Tiles with fixed objects

A tile may have a three-dimensional physical object permanently attached to it.

Examples include:

- trees,
- tables,
- chests,
- buildings.

If the physical object occupies the space needed by a figure, a figure cannot occupy that cell.

This is a physical constraint, independently of any gameplay meaning the object may later have.

## Edges

The boundary between two orthogonally adjacent cells is a single shared edge in the game model.

An edge may contain physical features such as:

- a wall,
- a door,
- an opening such as a window.

Each edge type defines two independent base properties:

- **Passable** — whether movement may cross the edge.
- **Blocks LOS** — whether the edge blocks Line of Sight.

The initial edge types are:

| Edge | Passable | Blocks LOS |
| --- | --- | --- |
| None | yes | no |
| Wall | no | yes |
| Closed door | no | yes |
| Open door | yes | no |
| Wall with window | no | no |

Walls and similar features may physically be attached to one of the adjacent terrain tiles, but this construction detail does not change the logical model: the edge is shared by both cells.

A corner tile may therefore physically carry features on two of its edges.

### Doors

Doors are physically capable of being opened and closed.

Their current physical state is therefore part of Physical State.

The gameplay consequences of an open or closed door are rules and are deliberately not defined here.

### Windows and openings

Walls may contain openings such as windows.

Their effects on movement, line of sight, attacks, or other rules are deliberately deferred.

## Figures

### v0 footprint

The initial implementation supports only figures with a 1 × 1 footprint.

Larger figures are expected in the future, so the overall model should not assume that all figures must always be 1 × 1. Support for larger footprints is not required in v0.

### Position

A 1 × 1 figure on the board occupies exactly one grid cell.

Two figures cannot occupy the same cell.

A figure cannot occupy an intermediate position between cells or partially occupy multiple cells.

How movement between cells works is a gameplay rule and is not defined here.

### Facing

Figures have no gameplay-facing or orientation.

A physical miniature may visually face any direction, but its rotation has no meaning in the game state.

### Physical posture

A figure can physically be in one of two postures:

- upright,
- lying.

This posture is part of Physical State because it is directly observable on the table.

The gameplay meaning of either posture is deliberately not defined here.

## Units

Heroes and Monsters are both Units and use the same underlying rules model.

A Unit has:

- the common Unit stat schema,
- a set of abilities,
- Rules/Game State associated with the Unit,
- a physical figure when the Unit is present on the board.

Heroes and Monsters should not have parallel implementations of the same mechanics.

### Heroes and Monsters

The fundamental distinction between a Hero and a Monster is agency, not mechanics.

For a Hero, a player makes decisions among the choices permitted by the rules.

For a Monster, decisions are determined by that Monster's behavior rules. Monster behavior may determine, for example:

- movement choices,
- target selection,
- which ability to use,
- special decision overrides.

Monster behavior must be possible to follow manually in the physical game. It must not depend on opaque computer-only decision making.

Random choices or outcomes may be used only where the rules specify a corresponding physical random mechanism.

Monster Behavior is defined per Unit Type. The engine generates the legal alternatives for a required decision; the active Monster's Behavior ranks or selects among those alternatives. Behavior does not reimplement movement legality, Line of Sight, attack legality, or other core rules.

Common Monster archetypes may provide reusable default preferences. For example, a basic Melee behavior may prefer destinations from which it can attack and otherwise prefer moving closer to a suitable hostile Unit. A basic ranged behavior may prefer positions with Line of Sight and a target within `RNG`, with a preference for an appropriate attack distance. Specific Unit Types may refine or override such preferences.

Monster preferences should be expressible as deterministic, manually followable priorities rather than opaque tactical utility calculations. Equal preferences are resolved by deterministic rule-defined tie-breaking. The final fallback may use the established top-to-bottom, left-to-right spatial ordering where appropriate.

The Decision Provider boundary does not require every provider to use Monster Behavior ranking. A future simulation or AI provider may choose among the same legal alternatives using a different strategy.

### Default Monster Behavior

For Unit selection, choose the engine-supplied eligible Unit whose current position is first in top-left board order (ascending `y`, then `x`). Eligibility and candidates are recomputed by the engine before every selection; no ordering is saved at the beginning of a group phase.

For movement, use these priorities:

1. If the Unit can make a normal Attack against a hostile Unit from its current position, stay when staying is allowed.
2. Otherwise, choose a legal movement destination from which a hostile Unit can be attacked. Prefer the shortest movement path, then top-left board order (top to bottom by row, then left to right within the row; ascending `y`, then `x`).
3. Otherwise, choose the legal movement destination with the shortest remaining traversable path to a position from which a hostile Unit can be attacked. Break ties by the shortest movement path for this activation, then top-left board order.
4. If no legal movement destination has a reachable attack position, stay when allowed. If staying is not allowed, the behavior cannot supply a choice.

Remaining traversable distance uses the Approach distance rules defined under Unit activation. It ignores Units as traversal obstacles while still respecting terrain and edge passability. It measures distance to a position from which the hostile Unit could be attacked, not ordinary legal movement into the hostile's occupied cell. An attack position must permit a normal Attack under currently supported rules; undefined LOS does not count as a possible attack.

When the Move decision permits no movement, remaining in the Unit's current cell participates in the same approach ranking as the engine-supplied movement destinations. Treat staying as movement path length 0 and evaluate its remaining approach distance from the current cell. If staying ranks best under the normal criteria, the Monster stays. This includes ties on remaining approach distance: because staying has path length 0, a Monster does not move when movement would leave it equally close to a future attack position.

Destination ties use top-left board order. Separately, canonical shortest movement paths retain the BFS neighbor expansion order top, left, right, bottom.

For Act, Default Monster Behavior first chooses among engine-supplied Normal Attack candidates using the attack ranking below. If no Normal Attack candidate exists and one or more Try Open Door candidates exist, it chooses Try Open Door. When several such door candidates exist, choose the candidate whose cell on the opposite side of the door is first in top-left board order (ascending `y`, then `x`). Default Monster Behavior acts on these reusable Action types and does not special-case the Unit Type that supplied them.

For Attack, choose the nearest engine-supplied legal target by Manhattan distance between the attacker's and target's current cells: `abs(dx) + abs(dy)`. Break ties by top-left board order (ascending `y`, then `x`). The special Melee range rule affects legality only: an orthogonally adjacent target ranks ahead of a diagonally adjacent target because their Manhattan distances are 1 and 2 respectively. Behavior does not determine attack legality.

### Abilities and behavior

Abilities define what a Unit can do or what effects it can produce.

Monster behavior defines how a Monster makes decisions about those capabilities.

These are conceptually separate even if a physical Unit card presents behavior text next to, or as part of, an ability for readability.

### Per-Unit state and physical identity

Persistent state associated with an individual Unit must be practically and unambiguously trackable for that specific physical figure.

Units belonging to an indistinguishable group must not carry persistent per-figure state that cannot be represented by the physical figure itself. The digital implementation must not distinguish otherwise identical physical figures by silently tracking state that players cannot identify on the tabletop.

Physical posture is permitted because `upright` or `lying` is directly represented by each figure itself.

For the same reason, ordinary grouped Monsters that are physically indistinguishable have `HP 1`. Any Damage that reduces such a Monster's current HP therefore kills it and removes its figure, so no persistent damage needs to be associated with a particular figure.

This is not a general restriction that Monsters must have `HP 1`. A physically unique Monster, such as a boss, may have higher HP when its individual current HP can be tracked unambiguously, for example on its own physical Unit card. Heroes may likewise have higher HP because each Hero is individually identifiable and its current HP can be tracked on its physical card.

Abilities and effects may make grouped Monsters easier or harder to damage or kill without requiring untrackable persistent per-figure state. Different `DEF` values are one example.

## Initial Monster content

### Zombie

Zombie is the first Monster Unit Type used to establish the reusable Action/Behavior composition pattern.

- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack; `TryOpenDoor(2/6)`.
- Behaviors: Approach Through Closed Doors.

Zombie itself has no bespoke pathfinding or Decision Provider implementation. Its door-oriented play emerges from the reusable Approach Through Closed Doors Behavior, the reusable Try Open Door Action, shared gameplay queries, and Default Monster Behavior's normal Action priorities. Approach Through Closed Doors affects only automated decision analysis; it does not make Closed Doors traversable under the movement rules.

### Skeleton Archer

Skeleton Archer is a ranged Monster Unit Type whose automated Behavior prefers to attack while keeping as much distance as possible.

- Stats: `MOV 3`, `RNG 4`, `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.
- Behaviors: Maximize Attack Distance.

During Move, **Maximize Attack Distance** considers every legal movement destination plus staying in the current cell when no movement is permitted as a choice. If one or more of those positions allow the Unit to make a legal Normal Attack, only those positions participate in this Behavior's preferred-position ranking. For each such position, measure the distance to every hostile Unit that could be legally attacked from that position and take the distance to the nearest such hostile. Choose the position that maximizes that nearest-attackable-hostile distance.

Ties use shortest actual movement path length, with staying treated as path length 0, then top-left board order (ascending `y`, then `x`).

This Behavior may therefore move a Skeleton Archer away from an enemy even when it can already attack from its current cell, but it never moves out of all legal Normal Attack positions merely to create more distance. If no legal reachable position, including staying, permits a Normal Attack, use the normal approach behavior instead.

Maximize Attack Distance is a Decision Provider preference, not an attack or movement rule. It uses shared gameplay queries for attack legality, distance, Line of Sight, and related calculations rather than reproducing those rules inside the provider.

## Unit stats

Every Unit has the same five base stats:

- `MOV` — Movement,
- `RNG` — Range,
- `ATK` — Attack,
- `DEF` — Defence,
- `HP` — Hit Points.

All five stats are integers.

Their base domains are:

- `MOV >= 0`. A Unit with `MOV 0` cannot move.
- `RNG >= 0`. A Unit with `RNG 0` cannot make a normal attack.
- `ATK >= 0`. A Unit with `ATK 0` cannot make a normal attack.
- `DEF >= 0`. `DEF 0` is a normal valid value.
- `HP >= 1`. Zero is not a valid HP stat value.

The HP stat and current HP are distinct concepts. Current HP is mutable Rules/Game State. Current HP cannot be reduced below zero. Under the normal rules, a Unit whose current HP reaches zero dies and its figure is removed from the board. Future abilities may explicitly modify this behavior.

The detailed gameplay meaning of the stats is defined by the corresponding rules rather than by these domain constraints.

## Rounds and activation order

Play is divided into Rounds.

Activation order is determined physically by drawing Activation Tokens from a bag.

### Activation Tokens

An Activation Token identifies a Unit Type, not an individual Unit instance.

At the start of a Round, the default rule is to place one Activation Token in the bag for each Unit Type that currently has at least one Unit in play.

Each Hero is a unique Unit Type and therefore normally has its own personal Activation Token.

Monsters are usually grouped by Unit Type. Multiple Units of the same Monster type share the same Activation Token. For example, any number of Skeleton Units normally contribute one Skeleton Activation Token to the bag.

One token per participating Unit Type is the default rather than a structural limit. Abilities and special rules may explicitly modify how many Activation Tokens a Unit Type contributes. A particularly dangerous Monster type could, for example, contribute an additional token and therefore activate more than once during a Round.

Tokens are drawn randomly from the bag without replacement. When a token is drawn, the Unit Type identified by that token activates.

When the bag is empty, the Round ends. The bag is then populated again for the next Round from the Unit Types that are still in play, applying any rules that modify their number of Activation Tokens.

If all Units of a type leave play after that type's token has already been placed in the bag, the token remains in the bag. If it is later drawn, the Unit Type activation is valid but affects zero Units and therefore does nothing. The bag does not need to be searched during a Round to remove such tokens.

How Unit Types entering play during an ongoing Round affect the bag is deliberately deferred until spawning or reinforcement rules require it.

### Unit Type activation

When a Unit Type activates, all Units of that type activate as a group.

Group activation is phase-based. All participating Units resolve a phase before the group proceeds to the next phase:

1. all Bonus Action phases,
2. all Move phases,
3. all Act phases.

This uses the same Bonus Action → Move → Act lifecycle defined for an individual Unit. A Hero Unit Type normally contains only one Unit, so the same group rules naturally reduce to a single Hero activation.

### Unit selection within a group phase

Every scenario has a defined physical orientation with one board corner designated **top-left**.

This establishes an unambiguous spatial ordering of cells: top to bottom by row, and left to right within each row. In a digital coordinate system where the top-left cell is `(0, 0)`, `x` increases to the right, and `y` increases downward, this ordering is equivalent to sorting by `(y, x)`.

The order in which Units resolve a group phase is not fixed at the beginning of that phase.

Before each Unit resolves the current group phase, the engine determines the Units of the active Unit Type that are currently eligible to resolve it and have not already completed that phase during the current activation. This concrete set is the legal decision space for choosing the next Unit.

The responsible Decision Provider selects the next Unit from that set. As with other decisions, the provider does not determine eligibility and the engine validates the returned selection against the supplied candidates.

Default Monster Behavior chooses the currently topmost eligible Unit, breaking ties by choosing the leftmost one. Because this choice is made again before each Unit resolves the phase, current board positions are used. Earlier movement during the same group phase can therefore affect which remaining Monster is selected next.

A human or future AI Decision Provider can receive the same eligible-Unit candidates. If the applicable rules permit free ordering for that form of agency, it may choose any eligible Unit rather than using the default Monster spatial ordering.

Once a Unit has completed the current group phase, it cannot be selected again during that phase. If a Unit ceases to be eligible before being selected, it is simply absent from the next candidate set.

The physical procedure therefore requires tracking which Units have already resolved the current group phase, but does not require remembering their positions or a precomputed ordering from the start of the phase.

A Unit Type containing zero eligible Units completes the current group phase immediately. When no eligible Units remain, the group proceeds to its next phase or completes the activation after Act.

The exact eligibility consequences of future spawning, summoning, reinforcement, or similar rules are deliberately deferred until such mechanics are introduced. In particular, no general v0 rule is imposed here about whether a newly introduced Unit can participate in an activation already in progress.

## Unit activation

Heroes and Monsters use the same activation structure.

A Unit activation always progresses through three phases in this fixed order:

1. **Bonus Action**
2. **Move**
3. **Act**

A phase still occurs when the Unit chooses to do nothing during that phase. This is significant for rules that may trigger before or after a phase.

### Bonus Action

During the Bonus Action phase, a Unit may use zero or one ability explicitly marked `Bonus Action` on its Unit card.

There is no general set of Bonus Actions. A Unit can perform a Bonus Action only when its card provides an applicable Bonus Action ability.

If several Bonus Action abilities are available, at most one may be used during the activation.

For a Hero, the player chooses whether and which available Bonus Action to use. For a Monster, its behavior rules determine that choice.

### Move

During the Move phase, a Unit moves from zero through `MOV` steps.

Each step moves to an orthogonally adjacent cell. Diagonal movement is not allowed.

The complete movement is one atomic rules operation. The path may contain multiple cells and is relevant when determining whether the movement is legal, but gameplay effects do not occur between individual steps of the movement.

Rules may therefore trigger before or after the complete movement, but not in the middle of it unless a future rule explicitly overrides this principle.

Choosing zero steps still constitutes completing the Move phase and the movement operation. Consequently, a future rule triggered after movement may still trigger when the Unit moved zero steps.

Each movement step crosses the shared edge between the current cell and an orthogonally adjacent destination cell. The step is legal only if that edge is passable for movement.

For the currently defined edge features:

- a wall is impassable,
- a closed door is impassable,
- an open door is passable.

Other edge features may define their own movement passability when introduced.

A Unit may pass through cells occupied by friendly Units during its movement.

A Unit may not pass through cells occupied by hostile Units. Hostile Units can therefore block movement paths.

A Unit may never end its movement in a cell occupied by another Unit, whether friendly or hostile.

Whether a destination cell is otherwise passable is determined by the rules for its terrain or fixed object. Those rules are deliberately deferred to the corresponding terrain specification.

### Movement and distance queries

Different rules questions use different notions of pathing and distance. They must not be treated as one interchangeable pathfinding operation merely because they can share low-level grid traversal algorithms.

**Actual movement** answers where a Unit can legally move during its Move phase. It respects terrain and edge passability, the Unit's effective movement allowance, and current Unit occupancy. Friendly Units may be passed through, hostile Units may not be passed through, and movement may not end on any occupied cell.

**Approach distance** answers how far a cell is from a goal through the board's traversable terrain. It is used for evaluations such as deciding which legal movement destination brings a Monster closer to a future attack position. Approach distance respects terrain and edge passability but ignores Units as traversal obstacles. Figures are temporary occupants and must not make the underlying route appear permanently unreachable. This allows Monsters behind other Monsters, for example in a doorway or corridor, to continue moving toward the same engagement even when the front Monsters currently occupy the route.

Gameplay queries are neutral analysis tools over game state. A query does not decide which game rule or Behavior applies and should not infer such policy from a specific Unit Type identity. Instead, its caller supplies the parameters appropriate to the question being asked. Rules/engine code may call the same query with parameters dictated by the actual game rules, while a Decision Provider may call it with parameters dictated by the Behavior it is evaluating. The query owns the shared calculation; callers do not duplicate pathfinding, Line of Sight, legality, or other underlying algorithms.

For automated Zombie movement analysis, the **Approach Through Closed Doors** Behavior tells the provider to request approach distance with Closed Door edges treated as traversable. This affects only that analysis. Closed Doors remain impassable for actual movement because movement legality is evaluated using the parameters required by the movement rules.

When an approach-distance query measures distance to a specific goal cell, that goal cell itself need not be passable or currently occupiable in order for its distance to be measured. It is treated as reachable as the endpoint for that calculation only; an otherwise impassable goal cell does not become traversable and cannot be used as a route through to cells beyond it.

**Attack range** is not a pathing query. Ranged attack distance is Manhattan distance regardless of terrain, edges, or Units. Obstacles affect attack legality separately through Line of Sight and other attack rules. The special `RNG 1` Melee rule remains a separate range-legality rule.

### Reachable destinations and canonical paths

A movement decision normally chooses a destination, not a path.

The engine determines all cells reachable by a Unit within its effective movement allowance while applying movement legality. Each reachable destination is exposed at most once.

For each reachable destination, the engine associates one deterministic shortest legal path from the Unit's starting cell to that destination. A flood-fill or breadth-first search is an appropriate implementation for the current uniform-cost grid movement.

A Unit therefore does not spend unnecessary movement by taking a longer route when a shorter legal route reaches the same chosen destination.

If several equally short legal paths reach the same destination, the engine selects one using a fixed deterministic tie-breaking order.

The canonical path is retained as meaningful rules-result information even though the decision normally selects only the destination. This allows, for example, a graphical presentation to animate the individual steps of an otherwise atomic Move.

If future rules make alternative paths to the same destination meaningfully different, path choice can be revisited explicitly at that time rather than being part of the v0 decision space.

### Act

During the Act phase, a Unit may perform zero or one Action.

All available Actions compete for the same single Action opportunity. A Unit cannot perform a basic Action and a Unit-specific Action during the same Act phase unless a future rule explicitly overrides this limit.

A normal **Attack** is a basic Action available through the general rules.

A Unit card may provide additional abilities explicitly marked `Action`. Using one of these consumes the Unit's Action for the phase.

The reusable **Try Open Door** Action is available only to Unit Types that have that Action. It is legal when the Unit occupies either cell bordering a Closed Door edge. Resolving it consumes the Unit's Action and rolls one ordinary six-sided die. The Action has a success count from 0 through 6; that many faces are designated as success faces. On success, the Closed Door becomes an Open Door and the normal door-open gameplay event is emitted. On failure, the door remains closed. The Action is consumed in either case.

The first use of this Action is `TryOpenDoor(2/6)` for Zombies.

## Normal attacks

A normal Attack is a basic Action.

A Unit can make a normal Attack only when both its effective `RNG` and effective `ATK` permit it. Base stats may be modified by future rules or abilities; attack resolution uses the resulting effective stat values. The rules for combining modifiers are deliberately deferred until needed.

### Range

For ranged attacks, `RNG` is the maximum orthogonal distance between attacker and target. Orthogonal distance is Manhattan distance: the absolute horizontal difference plus the absolute vertical difference between their cells.

`RNG 1` is a special case called **Melee**. A Melee attack may target any of the eight cells immediately surrounding the attacker, including diagonally adjacent cells. A physical Unit card may display `Melee` rather than the numeric value `1` to make this distinction explicit.

A target must satisfy both the applicable range rule and Line of Sight.

### Line of Sight

Line of Sight (LOS) is a straight geometric line from the center of the attacker's cell to the center of the target's cell.

Range measurement and LOS are separate tests.

LOS blocking is based on logical grid geometry rather than the detailed physical silhouette of a miniature or terrain model.

A cell is either completely LOS-blocking or completely non-blocking. If a type of terrain or fixed object is defined as LOS-blocking, the entire area of its cell blocks LOS regardless of the object's actual physical shape within the cell.

Likewise, an edge feature either blocks LOS at the point where the LOS line crosses that edge or does not. For example, an open door does not partially obstruct an edge merely because a physical door model remains present.

An open door does not block LOS. Opening a door therefore immediately allows otherwise legal attacks through that edge, including hypothetical attacks used to rank Monster movement destinations.

Because LOS runs between cell centers, it cannot run along a grid edge. It can, however, pass exactly through a grid corner.

When LOS passes exactly through a corner, it is blocked only when all possible passages through that corner are blocked. If at least one passage is free, LOS passes through the corner. This also applies when a wall terminates exactly at the corner.

Merely touching the corner of an LOS-blocking cell does not count as passing through that cell's interior.

Friendly Units do not block LOS.

Whether hostile Units block LOS is deliberately not yet defined.

Which terrain types, fixed objects, walls, doors, windows, and other edge features block LOS will be defined by their corresponding rules.

### Attack and defence dice

The attacker rolls a number of Attack Dice equal to its effective `ATK`.

An Attack Die is a physical six-sided die with:

- 3 Hit faces,
- 3 Miss faces.

The defender rolls a number of Defence Dice equal to its effective `DEF`.

A Defence Die is a physical six-sided die with:

- 2 Block faces,
- 4 Miss faces.

Count the total Hits and Blocks rolled.

Damage is:

`max(0, Hits - Blocks)`

The defender's current HP is reduced by the resulting Damage, but never below zero.

A Unit with effective `ATK 0` cannot make a normal Attack. `DEF 0` is valid and means that the defender rolls zero Defence Dice.

### Death

Under the normal rules, when a Unit's current HP reaches zero, that Unit dies and its figure is removed from the board.

This rule applies equally to Heroes and Monsters.

Future abilities or special rules may explicitly alter what happens when a Unit would die, but no such exceptions are part of the base rules yet.

## Physical component constraints

The abstract game model and the available physical components are separate concerns.

The rules may permit a world configuration that cannot be constructed using the currently manufactured set of terrain pieces.

For example, a wall edge may be valid in the abstract model, while no physical water tile with an attached wall exists. With the current component set, that means such a map configuration cannot be physically constructed.

Component availability should therefore constrain physical scenario construction rather than silently becoming a universal game rule.

This separation also allows the physical component set to grow without requiring fundamental rule changes.

## Loose tokens and markers

No loose tokens or markers are currently planned as part of the board representation.

They may be introduced later. The architecture should not unnecessarily prevent their addition, but v0 does not need a generic token system.

## Explicitly deferred

The following are intentionally not specified yet:

- terrain effects beyond the currently defined Passable and Blocks LOS properties,
- LOS-blocking behavior of hostile Units,
- LOS effects beyond the currently defined terrain and edge properties,
- stat modifier and effective-stat calculation rules,
- edge effects beyond the currently defined Passable and Blocks LOS properties and the Open Door action,
- gameplay meaning of upright and lying figures,
- larger-than-1×1 figure behavior,
- loose tokens and markers,
- exact terrain and edge-feature taxonomies,
- scenario validation against an inventory of physical components.

These should be introduced only when the corresponding game rules or content require them.
