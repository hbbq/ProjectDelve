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

### No hidden remembered game state

Players must not be required to remember persistent game state that is not represented by the physical game.

At stable boundaries, the board and physical components must contain all information required to continue play correctly. In particular, once a turn or other currently resolving sequence has completed, players must be able to leave the game indefinitely, return later, inspect the table, and continue correctly without remembering what happened previously. Conceptually, a finished turn could be left for years and play resumed by drawing the next Activation Token.

Short-lived progress within the sequence currently being resolved may be kept mentally when it is natural and unambiguous. Current examples include whose turn or activation is in progress, which Units of the currently resolving Unit Type have already activated during that activation, and whether the currently active Unit has already completed its Move or Action, and which individual Bonus Actions it has already used during that activation. If such information must survive a stable stopping point, however, it must be represented explicitly.

Persistent or cross-turn information such as HP, remaining ability uses, acquired abilities, changed stats, statuses, opened doors, spawned or removed Units, and similar state must therefore have a physical representation rather than relying on player memory.

Strategic intent is not game state. Players may of course remember or forget plans, deductions, priorities, and intended future actions; the game need not physically record what a player was planning to do.

### Unit posture

Every Unit figure has a physical **Posture**: **Upright** or **Lying**. Posture is part of observable Physical State and therefore requires no separate status token or remembered duration.

An Upright Unit functions normally.

A Lying Unit remains in play, remains a Unit of its Side and Unit Type, occupies its Cell normally, and may be targeted and attacked normally. Its Stats and current HP continue to exist and are used normally, including effective values produced by rules originating elsewhere.

While Lying, the Unit cannot Move, Attack, or use Actions, Bonus Actions, or Free Actions. Its own Passives, Capabilities, and Behavior are inactive and produce no effects or opportunities. Rules originating from other Units or from the scenario may still affect the Lying Unit normally. Merely being Lying does not cause the figure to stop counting as a Unit for occupancy, friendliness/hostility, adjacency, targeting, or conditions on another Unit's rules.

When a Lying Unit would activate, stand its figure Upright and immediately complete that Unit's activation. It receives no Move, Action, Bonus Action, Free Action, or other ordinary activation opportunity from that activation. If one Unit Type activation contains several Units of that type, each Lying Unit is still activated in the normal sequence; its individual activation consists only of standing up.

Rules may directly change Posture by instructing the player to **lay down** an Upright Unit or **stand up** a Lying Unit. A scenario may also begin with Units already Lying, and future rules may place or create Units in either Posture when explicitly specified.

Posture describes the physical state rather than a cause such as stunned, knocked down, sleeping, or spawned. Concrete content should use the physical operations `lay down` and `stand up` unless it genuinely needs additional rules.

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

Control/agency is assigned independently of Side and Unit Type. Side describes game relationships such as friendly and hostile; it does not imply human or automated control. Likewise, being Hero, Monster, companion, boss, or other content does not select a different activation model. All Units use the same activation, legality, Actions, Capabilities, and rules resolution regardless of which Decision Provider controls them. A scenario may therefore mix forms of agency on either Side: several humans may each control individual Units, a human may control a boss while ordinary Units on the same Side use automated Behavior, or a rule may temporarily transfer control of a Unit to another provider without changing what that Unit can legally do.

Behavior is a policy for automated agency, not an intrinsic restriction on the Unit. It may express the Unit's intended character or play style by choosing among its existing legal choices. For example, a Goblin's Capability may permit an extra Move after an Attack while its default Behavior uses that opportunity to retreat; a different automated Behavior or a human controller may use the same legal opportunity more aggressively.

Automated Behavior may depend on observable and physically trackable game state. This allows physical content such as companions or bosses to change decision policy according to HP, a tracker, a phase, activation parity, or another explicitly represented condition while remaining manually executable at the table. A physical card may, for example, be flipped when a boss enters another phase and provide a different Behavior on its reverse side. Such a Behavior change is distinct from changing the boss's Stats, Actions, or Capabilities: only the latter changes the Unit's actual rules and legal possibilities.

Default automated content intended for ordinary physical play should remain simple, deterministic, and practical to execute manually. More sophisticated digital Decision Providers may make smarter selections among the same legal choices without creating different underlying game rules. Concrete companions, boss phases, control-transfer abilities, and similar content should define the smallest additional state or rules they actually require rather than introducing a speculative general controller or phase framework.

Examples include:

- which Unit is selected when a rule requires a choice,
- which reachable destination a Unit moves to,
- which legal target it attacks,
- which Action or ability it uses,
- any other rules-defined choice.

For v0 movement, the engine supplies the legal reachable destinations, each with its canonical path. The provider chooses a destination or no movement when permitted.

For Action resolution, the engine supplies the complete legal set of action candidates from the Unit Type's Actions. Normal Attack candidates apply Range, Line of Sight, hostility, and all other attack-legality rules. Other Actions supply their own legal candidates and targets.

A provider does not establish or extend legal choices. Legal candidate generation is authoritative game rules.

A legal choice may additionally carry **relevance** metadata. Relevance does not change legality or player agency: all legal choices remain part of the authoritative decision space and may be exposed to and selected by an external Decision Provider or client. Relevance is a deliberately shallow convenience policy describing whether a legal choice is meaningful enough to require a normal decision stop. It is not tactical evaluation.

Relevance asks whether there is a concrete reason to consider the choice for its ordinary intended gameplay effect, not whether the choice is strategically good. It deliberately does not attempt tactical evaluation, probability thresholds, or optimization. For example, Rage remains relevant when an Attack can use its `ATK` modifier even if the attacker already has overwhelmingly more `ATK` than the defender has `DEF`; the modifier can still affect the Attack, so whether spending Rage is worthwhile belongs to the player or Decision Provider.

A relevance rule should use the simplest evaluation that correctly represents the concrete content. Sometimes current state is sufficient: a hypothetical `Heal 1 HP` could simply be irrelevant when current HP already equals maximum HP. For deterministic temporary stat effects that are safe to evaluate shallowly, relevance may instead apply the complete effect to a hypothetical copy of state and compare the authoritative gameplay opportunities available before and after it. This avoids duplicating simplified versions of movement, attack, targeting, Line of Sight, activation timing, or other legality rules inside relevance logic.

For the currently supported temporary stat effects, the comparison asks whether the complete hypothetical effect creates at least one new legal Move destination, creates at least one new legal Normal Attack target, or improves a Normal Attack against a target legal in both states. It observes resulting gameplay opportunities rather than inspecting which stat modifiers or modifier signs caused them. Whether Move or Attack is currently available comes from the same authoritative candidate generation used for normal decisions; relevance does not separately infer availability from activation flags.

All modifiers belonging to one effect are applied together before relevance is evaluated. Tradeoffs remain player decisions: for example, an effect that increases `RNG` while decreasing `ATK` is relevant if it opens a new legal target. Relevance does not combine benefits and drawbacks into a strategic score or decide whether that new target is worth weaker attack odds.

Attack-effectiveness comparison should use the authoritative values that attack resolution actually uses. Under the current rules, increased effective `ATK` against the same legal target is an improvement because it rolls more Attack Dice. If future rules introduce effects such as effective-ATK caps or changes to a target's effective `DEF`, shared attack-resolution queries should expose the resulting attack inputs rather than relevance duplicating those rules. An unambiguous improvement in those inputs may establish relevance; mixed tradeoffs, such as fewer Attack Dice together with fewer Defence Dice, are left to the player rather than reduced to probability or utility.

Hypothetical relevance is deliberately conservative and is not a universal simulator or planner. It may filter a choice only when a shallow, rules-authoritative check can safely establish that its ordinary effect is ineffectual or unambiguously detrimental. It does not follow future decision chains, optimize tactics, resolve hypothetical randomness, or reveal information that the player does not yet have. When an effect is complex, intentionally puzzle-like, random, information-revealing, or otherwise unsafe to classify this way, the legal choice remains relevant rather than being filtered. A future effect that cycles several stat values may therefore deliberately always remain relevant so the player evaluates the tradeoffs, while a future effect such as `roll a d6: ATK has that value this turn` must not roll hypothetically merely to decide relevance. These are boundary examples, not current abilities.

Relevance is intentionally allowed to ignore unusual strategic value caused only by secondary state changes. For example, using Rage with no possible Attack still spends one of its limited uses and therefore changes Game State, and a future rule might even reward exhausting all uses. Rage may nevertheless remain irrelevant in that situation because its ordinary intended `ATK` effect cannot be used. Supporting every such interaction would turn relevance into strategic reasoning rather than a shallow default decision-stop policy. A player who wants access to these unusual but legal choices can disable relevance-based automatic progression or presentation filtering; legality and explicit submission remain authoritative and unchanged.

For example, a temporary `ATK` bonus may be legal but irrelevant when no legal Attack choice remains, while a temporary `MOV` bonus may be irrelevant after the Unit has already completed its Move. Relevance need not determine whether extra movement reaches a useful cell or whether an attack bonus is likely to overcome a particular defence or effect.

Automatic progression may optionally operate on the relevant subset without removing other legal choices from the authoritative decision space. In particular, when exactly one relevant choice remains, that choice may be selected automatically even if additional legal-but-irrelevant choices exist. The semantics of a future state containing zero relevant choices while legal choices remain are deliberately deferred until concrete game content requires them.

Presentation filtering is separate again. A client may choose to hide legal choices marked irrelevant, or may display them differently, without changing engine legality or submission validation. Such a setting is presentation state rather than Rules/Game State and must not alter which choices are legal or available for explicit submission.

This distinction is summarized as: **legality determines agency; relevance may determine decision stops; presentation determines which supplied choices are shown.**

The first concrete relevance-sensitive content is the Barbarian ability **Rage**. Rage is a Bonus Action with two uses per game and applies `+2 ATK this turn`. Rage may remain legal even when no Attack is available, while being marked irrelevant because the modifier can no longer affect an Attack during that activation. A state with legal choices such as irrelevant Rage plus relevant End Turn should therefore be able to auto-progress through End Turn without making Rage illegal or unavailable to a client when automatic progression is disabled.

Presentation relevance must not silently become ability timing. An ability does not acquire a rule such as `BeforeMove` or `BeforeAttack` merely because normal presentation hides it or automatic progression skips a decision stop when its ordinary purpose can no longer affect the remaining activation. Explicit timing restrictions should exist only when the physical game rule actually requires them.

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

### Intermediate authoritative state during presentation

A single engine run may resolve several gameplay events before reaching the next stable state. Presentation may need to show authoritative state changes at the point where those events occur rather than displaying only the final state after the complete event sequence has been presented. For example, if several Monsters damage the same Hero during one engine run, a graphical client should be able to show the Hero's authoritative current HP changing between those attack presentations.

The client must not reconstruct these intermediate states by applying Rules/Domain Events as state mutations. An event such as damage, movement, opening a door, or death describes what happened; it does not require the client to know the corresponding rules for mutating Game State.

Instead, an engine result may associate presentation-relevant resolution steps with an authoritative Game State snapshot representing the state after that step. Conceptually:

```text
initial authoritative state
    -> event / presentation step
    -> authoritative state after that step
    -> event / presentation step
    -> authoritative state after that step
    -> ...
    -> final stable authoritative state and next required input
```

This may be represented by a result-level structure such as a sequence of resolution steps containing event information together with `StateAfter`. The exact transport shape is an implementation concern; the important rule is that intermediate state comes from the rules engine rather than being derived by the client.

Rules/Domain Events remain semantic descriptions of resolved gameplay and should not become generic state patches merely to support presentation. Likewise, state snapshots are not themselves gameplay events. Keeping these concepts separate allows a client to animate or narrate an event and then render the authoritative state for that point without learning how the event changes HP, positions, doors, status, or other game state.

The final Game State returned by the engine remains authoritative and must correspond to the state after all resolved steps in that engine run.

### Client boundary and presentation

A client may understand game-domain data in order to present it well, but it must not need to understand game rules in order to play the game correctly.

The client consumes authoritative state to render the current game, presents the choices supplied by the rules engine, submits the selected choice, and presents the resulting Rules/Domain Events. It must not derive additional legality, turn progression, or rules consequences from state on its own.

Domain-specific presentation is explicitly allowed. For example, a client may know that HP can be shown as a health bar, that a Door has an open or closed visual form, or that a UnitMoved event can be animated along its path. Such knowledge affects presentation only.

State values must not be treated as implicit rules by the client. For example, HP reaching 0 does not by itself authorize the client to remove, disable, or skip a Unit. A future rule may allow a Unit with 0 HP to remain in play or activate normally. Likewise, a stat value, terrain property, or other visible state must not be used by the client to infer what choices are legal unless the engine supplied those choices.

The boundary is therefore:

- **State** says what the authoritative game currently looks like.
- **Choices** say what external agency may choose now.
- **Choice** is the external input returned to the engine.
- **Events** say what the engine resolved while advancing to the next stable state.

Game-specific rendering and animation may be rich and specialized. Rules legality and progression remain authoritative in the rules engine.

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

For Unit selection, choose the engine-supplied eligible Unit whose current position is first in top-left board order (ascending `y`, then `x`). Eligibility and candidates are recomputed by the engine before every selection; no ordering is saved at the beginning of a Unit selection.

For movement, use these priorities:

1. If the Unit can make a normal Attack against a hostile Unit from its current position, stay when staying is allowed.
2. Otherwise, choose a legal movement destination from which a hostile Unit can be attacked. Prefer the shortest movement path, then top-left board order (top to bottom by row, then left to right within the row; ascending `y`, then `x`).
3. Otherwise, choose the legal movement destination with the shortest remaining traversable path to a position from which a hostile Unit can be attacked. Break ties by the shortest movement path for this activation, then top-left board order.
4. If no legal movement destination has a reachable attack position, stay when allowed. If staying is not allowed, the behavior cannot supply a choice.

Remaining traversable distance uses the Approach distance rules defined under Unit activation. It ignores Units as traversal obstacles while still respecting terrain and edge passability. It measures distance to a position from which the hostile Unit could be attacked, not ordinary legal movement into the hostile's occupied cell. An attack position must permit a normal Attack under currently supported rules; undefined LOS does not count as a possible attack.

When the Move decision permits no movement, remaining in the Unit's current cell participates in the same approach ranking as the engine-supplied movement destinations. Treat staying as movement path length 0 and evaluate its remaining approach distance from the current cell. If staying ranks best under the normal criteria, the Monster stays. This includes ties on remaining approach distance: because staying has path length 0, a Monster does not move when movement would leave it equally close to a future attack position.

Destination ties use top-left board order. Separately, canonical shortest movement paths retain the BFS neighbor expansion order top, left, right, bottom.

For Action, Default Monster Behavior first chooses among engine-supplied Normal Attack candidates using the attack ranking below. If no Normal Attack candidate exists and one or more Try Open Door candidates exist, it chooses Try Open Door. When several such door candidates exist, choose the candidate whose cell on the opposite side of the door is first in top-left board order (ascending `y`, then `x`). Default Monster Behavior acts on these reusable Action types and does not special-case the Unit Type that supplied them.

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

## Initial Unit content

The current v0 content roster is deliberately small. The reference implementation and its default test/playtest scenario should use only the Unit Types listed in this section unless a test defines a temporary Unit Type specifically to exercise a rule.

### Heroes

#### Barbarian

- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 4`, `DEF 3`, `HP 5`.
- Actions: Normal Attack.
- Free Actions: Open Door.

#### Rogue

- Stats: `MOV 4`, `RNG 1` (Melee), `ATK 3`, `DEF 2`, `HP 4`.
- Actions: Normal Attack.
- Free Actions: Open Door.

#### Wizard

- Stats: `MOV 2`, `RNG 4`, `ATK 3`, `DEF 2`, `HP 4`.
- Actions: Normal Attack; Fireball.
- Bonus Actions: Focus.
- Free Actions: Open Door.

### Monsters

#### Grunt

Grunt is the baseline Monster Unit Type with no special Actions, Capabilities, or Behaviors.

- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.

#### Zombie

Zombie is the first Monster Unit Type used to establish the reusable Action/Behavior composition pattern.

- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack; `TryOpenDoor(2/6)`.
- Behaviors: Approach Through Closed Doors.

Zombie itself has no bespoke pathfinding or Decision Provider implementation. Its door-oriented play emerges from the reusable Approach Through Closed Doors Behavior, the reusable Try Open Door Action, shared gameplay queries, and Default Monster Behavior's normal Action priorities. Approach Through Closed Doors affects only automated decision analysis; it does not make Closed Doors traversable under the movement rules.

#### Skeleton Archer

Skeleton Archer is a ranged Monster Unit Type whose automated Behavior prefers to attack while keeping as much distance as possible.

- Stats: `MOV 3`, `RNG 4`, `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.
- Behaviors: Maximize Attack Distance.

During Move, **Maximize Attack Distance** considers every legal movement destination plus staying in the current cell when no movement is permitted as a choice. If one or more of those positions allow the Unit to make a legal Normal Attack, only those positions participate in this Behavior's preferred-position ranking. For each such position, measure the distance to every hostile Unit that could be legally attacked from that position and take the distance to the nearest such hostile. Choose the position that maximizes that nearest-attackable-hostile distance.

Ties use shortest actual movement path length, with staying treated as path length 0, then top-left board order (ascending `y`, then `x`).

This Behavior may therefore move a Skeleton Archer away from an enemy even when it can already attack from its current cell, but it never moves out of all legal Normal Attack positions merely to create more distance. If no legal reachable position, including staying, permits a Normal Attack, use the normal approach behavior instead.

Maximize Attack Distance is a Decision Provider preference, not an attack or movement rule. It uses shared gameplay queries for attack legality, distance, Line of Sight, and related calculations rather than reproducing those rules inside the provider.

#### Goblin

Goblin is the first Monster Unit Type with a Capability that changes the normal phase flow.

- Stats: `MOV 4`, `RNG 1` (Melee), `ATK 2`, `DEF 2`, `HP 1`.
- Actions: Normal Attack.
- Capabilities: `MoveAfterAttack(1)`.
- Behaviors: Back Away After Attack.

**Move After Attack** is a reusable Capability parameterized by the maximum number of movement steps. If the Unit performed an Attack as its Action, that Attack first resolves completely according to the normal resolution-timing rules. Immediately after that Action is complete, before the Unit's activation can finish or another Unit can be selected, that same Unit receives one additional Move with its movement allowance limited to the Capability's value. For Goblin this is `MOV 1`.

The additional Move uses the normal movement rules and legal-destination generation except for its reduced movement allowance. It is part of the game rules and therefore occurs regardless of which Decision Provider controls the Unit. If the Unit did not perform an Attack as its Action, no additional Move is created.

**Back Away After Attack** is the Default Monster Behavior for choosing during that additional Move only. It does not affect Goblin's ordinary Move.

For this Behavior, a hostile Unit counts as a nearby threat when it occupies one of the eight cells surrounding the Goblin and there is normal geometric Line of Sight between the hostile's cell and the Goblin's cell. Geometric adjacency alone is not sufficient: for example, a hostile on an adjacent cell separated by a blocking Wall or Closed Door does not count. Use the shared Line of Sight query with the ordinary/default geometric Line of Sight assumptions.

This is deliberately not a query about whether that particular hostile Unit could legally attack the Goblin. The hostile's Actions, Range, Capabilities, special attack rules, or other Unit-specific effects do not affect Back Away After Attack. Likewise, this Behavior does not define or depend on a general perception or visibility system; future concepts such as invisibility do not implicitly change it.

If one or more such nearby hostile threats exist after the Goblin's Attack has resolved, consider the legal destinations for its additional Move where no hostile Unit would be both eight-cell adjacent and connected to the Goblin by normal geometric Line of Sight. If at least one such destination exists, choose among those destinations using shortest actual movement path length and then top-left board order (ascending `y`, then `x`). If no such destination exists, choose no movement and stay.

If no such nearby hostile threat exists when the additional Move is being chosen, choose no movement and stay. Back Away After Attack does not try to maximize distance once the Goblin is outside this nearby-threat condition.

Back Away After Attack is only a Decision Provider preference. A Goblin controlled by another provider still receives `MoveAfterAttack(1)`, but that provider chooses how to use the additional Move. No general mechanism for assigning arbitrary Behaviors to arbitrary extra phases is required at this stage.

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

When a Unit Type activates, all living Units of that type participate in that Unit Type activation.

Units activate **one at a time**. One Unit completes its entire activation before another Unit of the same Unit Type is selected. Effects and physical changes produced by an earlier Unit are therefore already part of the board state when the next Unit is selected and activated.

The order is not fixed at the beginning of the Unit Type activation. Before each Unit activation, the engine determines the Units of the active Unit Type that are currently eligible and have not already completed an activation for this token. This concrete set is the legal decision space for choosing the next Unit.

The responsible Decision Provider selects the next Unit from that set. Default Monster Behavior chooses the currently topmost eligible Unit, breaking ties by choosing the leftmost one, using current board positions. A human or future smarter AI provider may choose a different eligible Unit when the rules permit it. Once a Unit has completed its activation for the current token, it cannot be selected again for that token.

This ordering is intentionally state-dependent. For example, one Zombie may open a door and a later Zombie of the same Unit Type may then move through that now-open door. Conversely, a different deterministic Unit order may produce a less advantageous sequence. Default Monster Behavior is intended to remain simple, deterministic, and manually followable in physical solo play rather than planning the complete group activation. A digital-only smarter provider may plan across several legal choices without changing the underlying game rules.

The exact eligibility consequences of future spawning, summoning, reinforcement, or similar rules are deliberately deferred until such mechanics are introduced.

## Unit activation

Heroes and Monsters use the same activation rules.

A Unit activation is driven by what the Unit has already done rather than by mandatory Bonus Action, Move, and Act phases. Conceptually the activation tracks at least whether the Unit has completed its Move, whether it has completed its Action, and whether it has used its Bonus Action opportunity.

A living Unit normally has:

- one Move opportunity,
- one Action opportunity after its Move has been completed,
- at most one Bonus Action when supplied by an applicable ability,
- any applicable Free Actions, subject to their own rules and usage limits.

After any choice resolves, the currently legal choices are determined again from the resulting state. The activation ends when the Unit chooses or automatically resolves **End Turn**. End Turn is legal only after Move (including Stay) is completed and all mandatory follow-up resolution has finished. In particular, Move After Attack resolves before End Turn or selection of another Unit; End Turn never starts an extra phase.

A Unit that dies during its own activation cannot continue acting.

### Passive abilities and state-dependent modifiers

A passive ability is continuously applicable while its stated conditions are true and does not consume Move, Action, Bonus Action, or Free Action opportunities.

The first passive ability introduced on a Hero is the Cleric's **Adjacent friendly Units get DEF +1**. The Cleric has base stats `MOV 3`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, and `HP 4`, and has the standard Hero Open Door Free Action.

For this passive, **adjacent** uses the same spatial relationship already used when determining whether a Unit is threatened by an adjacent Unit: the other Unit occupies one of the eight surrounding cells and normal Line of Sight exists between the two cells. Walls, doors, corners, and other LOS effects therefore affect this adjacency through the ordinary LOS rules rather than through a separate passive-specific approximation.

The Cleric grants `DEF +1` to each adjacent friendly Unit. The Cleric does not grant the bonus to itself because it is not adjacent to itself. Multiple applicable sources stack additively: for example, a friendly Unit adjacent to two Clerics receives `DEF +2`, and adjacent Clerics may grant `DEF +1` to each other.

State-dependent modifiers are derived from the authoritative Game State being evaluated rather than stored as independently authoritative state. Moving, death, changes to LOS, or other state changes therefore naturally change which modifiers apply. Effective-stat calculation and any shared gameplay queries must derive such modifiers from the specific Game State passed to them.

This also applies to copied or hypothetical Game States used by relevance evaluation. A state-dependent modifier must be calculated from the hypothetical state's own positions, Units, terrain, edges, and other relevant state, not from cached or precomputed information belonging to the original/live state. Implementations may optimize derived calculations later if necessary, but any cache must remain non-authoritative and preserve these semantics.

The Cleric also has the limited-use Action ability **Heal**:

```text
Heal [2/game]
Action

Restore up to 2 HP to an adjacent
damaged friendly Unit.
```

Heal starts each game with 2 remaining uses and a maximum of 2. It uses the Unit's normal Action opportunity, so using Heal consumes the Cleric's Action for that activation just as a normal Attack would.

A legal Heal target is a friendly Unit other than the Cleric that is adjacent under the same eight-surrounding-cells plus normal Line of Sight rule used by Aura, and whose current HP is below its maximum HP. A Unit at maximum HP is not a legal Heal target. Because the Cleric is not adjacent to itself, Heal cannot target the Cleric.

Resolving Heal immediately spends one use and restores up to 2 current HP to the chosen target, never increasing current HP above that Unit's maximum HP. A Unit missing only 1 HP is therefore a legal target and restores 1 HP. Heal involves no dice roll.

After Heal resolves, the Cleric's Action is complete and ordinary legal choices are generated again from the resulting state. Heal does not introduce a general healing, targeting, or effect framework beyond the rules required by this concrete ability.

The Cleric's third ability is **Holy Wave**:

```text
Holy Wave [2/game]
Action

Lay down all adjacent upright enemies.
Then lay down this Unit.
```

Holy Wave starts each game with 2 remaining uses and a maximum of 2. It uses the Unit's normal Action opportunity, so using Holy Wave spends one use and consumes the Cleric's Action. Holy Wave is not an Attack: it rolls no Attack or Defence Dice, deals no Damage, and does not create rules opportunities that require an Attack to have occurred.

Holy Wave lays down every adjacent hostile Unit that is currently Upright. Adjacent uses the established eight surrounding cells plus normal Line of Sight rule. Lying enemies are unaffected. After all affected enemies have been laid down, the Cleric is laid down as well.

Holy Wave remains legal when there are no adjacent Upright enemies, provided its normal use and Action requirements are satisfied. In that case it still spends one use, consumes the Action, and lays down the Cleric. Relevance may safely classify such a use as irrelevant when it has no ordinary beneficial effect; this does not change its legality.

Holy Wave is the first concrete ability that changes another Unit's Posture. It does not establish a general knockdown, stun, condition, saving-throw, resistance, or posture-effect framework beyond the base Posture rules.

The Wizard has the limited-use Action ability **Fireball**:

```text
Fireball [2/game]
Action

Choose a Cell within RNG and LOS.
Attack all Units on or adjacent to that Cell.
```

Fireball starts each game with 2 remaining uses and a maximum of 2. It uses the Unit's normal Action opportunity and is an Attack for rules that refer to an Attack. Using Fireball therefore spends one Fireball use and consumes the Wizard's Action.

The player chooses one target Cell within the Wizard's effective `RNG` and with normal Line of Sight from the Wizard's Cell. Range to the target Cell uses the ordinary ranged Manhattan-distance rule. Line of Sight is the established geometric Cell-to-Cell Line of Sight; Units do not create a separate visibility rule for Fireball. The target Cell may be empty, and Fireball remains legal even when its explosion would affect no Unit.

The explosion area is the target Cell and its eight surrounding Cells. A Unit in that area is a Fireball target only when normal Line of Sight exists from the target Cell to that Unit's Cell. Blocking terrain or edges may therefore protect a Unit in an otherwise adjacent Cell from the explosion.

Fireball targets **all Units** satisfying that area and Line of Sight rule, regardless of Side. Friendly fire therefore applies. The Wizard may also be a target of its own Fireball if its Cell lies in the explosion area and has the required Line of Sight from the target Cell.

Fireball is one Attack with multiple targets. Its Attack Dice use the attacker's general effective `ATK` when the Fireball begins, so ordinary this-turn modifiers such as Focus affect Fireball. The Attack Dice are rolled once for the whole Fireball and the resulting Hits are shared by every target. Target-specific attack modifiers do not independently change that shared roll unless a future rule explicitly says they apply to such an Attack.

Each target rolls its own effective `DEF` separately. Damage is calculated and applied separately to each target using the normal `max(0, Hits - Blocks)` rule, and normal death resolution applies. The complete target set is determined when the Attack begins, before Attack Dice, Defence Dice, Damage, or deaths are resolved.

Rules that occur after an Attack occur once after the complete Fireball has resolved, not once per target. Damage dealt to different targets remains separate for rules such as Cleave; Damage is not added across targets.

Fireball is the second concrete shared-roll multi-target Attack. Holy Wave and Fireball may share reusable implementation machinery where their now-concrete common structure warrants it, while their targeting, attack-value rules, content identity, and presentation remain distinct. This does not establish a universal area-effect or spell framework.

### Bonus Actions

Each individual ability explicitly marked `Bonus Action` may be used at most once during a Unit's activation. Different Bonus Action abilities do not compete for a shared Bonus Action opportunity and may be combined during the same activation. As a common baseline, a Bonus Action ability is legal while the Unit's activation is active, that specific ability has not already been used during the activation, and any required uses remain. Individual abilities may define additional legality conditions, such as requiring a valid target or a damaged Unit. Such restrictions are actual ability rules and are distinct from relevance; they should not be inferred merely because using an ability would normally be unhelpful.

Using a Bonus Action marks that specific Bonus Action ability as used for the current activation but does not prevent other Bonus Action abilities from being used. It does not by itself consume or complete Move or Action. After it resolves, legal choices are generated again from the new state.

There is no mandatory Bonus Action phase and no general `Skip Bonus Action` decision. Choosing another choice naturally allows the activation to progress without using a Bonus Action.

Ability names are content identity and may carry theme or lore without defining unique engine semantics. Different named abilities may therefore use the same underlying rules components. The first example is the Barbarian ability **Rage**:

```text
Rage [2/game]
Bonus Action
+2 ATK this turn
```

Rage starts each game with 2 remaining uses and has a maximum of 2 uses. Using Rage immediately consumes one remaining use and marks Rage as used for the current activation, then applies `+2 ATK` for the remainder of the current activation. The use is spent regardless of whether the Barbarian subsequently makes an Attack. Rage is legal while the Barbarian's activation is active, Rage has not already been used during that activation, and at least one Rage use remains; its relevance is separate from that legality.

Limited-use abilities track both maximum uses and remaining uses as Rules/Game State. A future rule may restore spent uses, but restoration cannot increase remaining uses above the ability's maximum. No general recharge or restoration rule is introduced by Rage itself.

Rage introduces the reusable effect concept **Modifier This Turn**, parameterized by a stat and signed amount; Rage applies `ModifierThisTurn(ATK, +2)`. A this-turn modifier is serializable Rules/Game State because it can affect later decisions and resolution during the activation. It contributes to the stat's effective value and is removed when that Unit's activation ends. Rage requires only this activation-duration modifier behavior; general modifier durations, stacking policies, priorities, sources, or a universal effect framework are deliberately deferred until concrete content requires them.

The Wizard has the Bonus Action ability **Focus**:

```text
Focus [2/game]
Bonus Action
+1 ATK this turn
```

Focus starts each game with 2 remaining uses and a maximum of 2. It uses the same reusable Bonus Action and Modifier This Turn rules as Rage rather than defining a separate mechanical effect. Using Focus spends one use, marks Focus as used for the current activation, and applies `ModifierThisTurn(ATK, +1)` for the remainder of that activation. Focus is therefore legal under the same per-ability Bonus Action rules and does not consume the Wizard's Action or prevent a different Bonus Action ability from being used during the same activation.

Focus contributes to the Wizard's general effective `ATK`. It therefore affects both normal Attacks and other Attacks that use general effective `ATK`, including Fireball.

Current HP remains mutable Unit state rather than being treated as a stat modifier merely because other stats may have effective values.

The Rogue has the Bonus Action ability **Dash**:

```text
Dash [2/game]
Bonus Action
+2 MOV this turn
```

Dash starts each game with 2 remaining uses and has a maximum of 2 uses. Using Dash immediately consumes one remaining use and marks Dash as used for the current activation, then applies `ModifierThisTurn(MOV, +2)` for the remainder of the current activation. Dash has no additional timing restriction: in particular, it remains legal after the Rogue has already completed its Move. In that situation the use can still be spent even though the movement modifier can no longer affect the already-completed Move.

Dash relevance is evaluated separately from legality. Relevance compares the Rogue's authoritative legal Move destinations in the current state with those produced from a hypothetical copy of state containing Dash's `MOV +2` modifier. Dash is relevant when the modifier makes at least one additional Move destination legal. If Move is no longer available, the authoritative gameplay query naturally supplies no Move opportunities in either state, so relevance needs no separate timing check. The comparison concerns destination choices rather than incidental representation such as a different canonical path to a destination that was already reachable. The hypothetical evaluation reuses normal authoritative candidate generation and does not duplicate movement, pathing, or activation rules inside Dash relevance.

The Rogue also has the Bonus Action ability **Throwing Knife**:

```text
Throwing Knife [2/game]
Bonus Action
RNG +2 & ATK -1 this turn
```

Throwing Knife starts with 2 remaining uses and a maximum of 2. Using it spends one use and marks Throwing Knife as used for the current activation, then applies both temporary modifiers for the remainder of the activation. Multiple modifiers from one ability are applied together as one effect package for effective stats and relevance, and are removed together when the activation ends.

Throwing Knife does not perform an Attack itself. It changes the Rogue's effective stats; a later normal Attack uses those values. With base `RNG 1` and `ATK 3`, the Rogue therefore has `RNG 3` and `ATK 2` after using it. Gaining a new legal Attack target is sufficient to make Throwing Knife relevant even though its attack strength is lower.

The Rogue also has the passive ability **Backstab**:

```text
Backstab
Passive
+1 ATK when attacking an enemy
that is adjacent to another friendly Unit.
```

Backstab is derived from the current Game State and the specific target of an Attack. It applies when the target is adjacent to at least one friendly Unit other than the attacking Rogue. Friendly is determined by Side relationship rather than Hero classification, controller, Unit Type, or Behavior. Multiple qualifying friendly Units still provide only a single `+1 ATK` bonus.

For Backstab, adjacent uses the established adjacency rule: one of the eight surrounding cells with normal Line of Sight between the cells. The Rogue itself does not satisfy the requirement merely by being adjacent to its target.

Backstab is target-specific rather than a general change to the Rogue's effective `ATK`. General effective stats describe the Unit before considering a particular Attack target; attack resolution may then apply additional target-specific modifiers to determine the authoritative Attack Dice for that combat. Backstab contributes `+1 ATK` at this target-specific stage.

For example, the Rogue normally has `ATK 3`, and attacks a qualifying Backstab target with 4 Attack Dice. After Throwing Knife, the Rogue's general effective stats are `RNG 3` and `ATK 2`; against a qualifying Backstab target that Attack therefore rolls 3 Attack Dice.

Any rule that evaluates the effectiveness of a specific Attack, including relevance comparison, uses the same authoritative target-specific attack values as actual attack resolution. Backstab must not be reimplemented as special relevance logic or inferred by presentation code.

### Free Actions

A Free Action consumes neither the Unit's Move opportunity nor Action opportunity and does not count as using any Bonus Action ability. It may have its own legality conditions, usage limits, exhaustion, or other restrictions.

Free Actions may be available at more than one point during an activation. After a Free Action resolves, legal choices are generated again from the resulting state.

Whether a particular Free Action is legal is part of the rules. Whether a legal Free Action is hidden by normal relevance filtering is a separate presentation/provider concern.

There is no general Free Action phase and no general `Skip Free Action` decision. A legal Free Action appears alongside the other choices currently available to the Unit. If several concrete uses of a Free Action are legal, each is a separate candidate.

The reusable **Open Door** Free Action is available only to Unit Types that explicitly have that Free Action. It is legal whenever the Unit occupies either cell bordering a Closed Door edge, and there is one legal Open Door candidate for each such Closed Door. Resolving the Free Action changes that edge to an Open Door and emits the normal door-open gameplay event.

Open Door has no additional activation-timing restriction. It may therefore be used before or after the Unit's Move and before or after its Action, whenever an adjacent Closed Door makes the Free Action legal and the Unit's activation has not ended. Opening a door consumes no Move or Action opportunity and does not count as using any Bonus Action ability. Legal choices are regenerated afterward, so opening one door may make another Open Door candidate available or change other legal choices.

Open Door is standard Hero content, but is represented explicitly on each Hero Unit Type rather than being inherited implicitly merely because the Unit Type is a Hero. The current Barbarian and Rogue Unit Types both have Open Door.

### Move

Before the Unit has completed its Move, it may choose one legal Move destination. Choosing to remain in the current cell is **Stay** and counts as completing the Unit's Move.

During a Move, a Unit moves from zero through `MOV` steps.

Each step moves to an orthogonally adjacent cell. Diagonal movement is not allowed.

The complete movement is one atomic rules operation. The path may contain multiple cells and is relevant when determining whether the movement is legal, but gameplay effects do not occur between individual steps of the movement.

Rules may therefore trigger before or after the complete movement, but not in the middle of it unless a future rule explicitly overrides this principle.

Choosing Stay still constitutes a completed Move and movement operation. Consequently, a future rule triggered after movement may still trigger when the Unit moved zero steps.

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

**Actual movement** answers where a Unit can legally move during its Move. It respects terrain and edge passability, the Unit's effective movement allowance, and current Unit occupancy. Friendly Units may be passed through, hostile Units may not be passed through, and movement may not end on any occupied cell.

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

### Action

After the Unit has completed its Move and before it has completed its Action, it may perform at most one Action.

All available Actions compete for the same single Action opportunity. A Unit cannot perform a basic Action and a Unit-specific Action during the same activation unless a future rule explicitly overrides this limit.

A normal **Attack** is a basic Action available through the general rules.

A Unit card may provide additional abilities explicitly marked `Action`. Using one consumes the Unit's Action opportunity.

The reusable **Try Open Door** Action is available only to Unit Types that have that Action. It is legal when the Unit occupies either cell bordering a Closed Door edge. Resolving it consumes the Unit's Action and rolls one ordinary six-sided die. The Action has a success count from 0 through 6; that many faces are designated as success faces. On success, the Closed Door becomes an Open Door and the normal door-open gameplay event is emitted. On failure, the door remains closed. The Action is consumed in either case.

The first use of this Action is `TryOpenDoor(2/6)` for Zombies.

### Immediate follow-ups

Some rules may create an immediate follow-up after another operation has fully resolved. A follow-up is not a separate Action or Bonus Action category. It is a continuation of the rule that created it and must be resolved or declined, when optional, before ordinary activation choices resume.

Follow-ups use the normal authoritative decision boundary when they require agency. A mandatory follow-up may permit only its follow-up choices; an optional follow-up also permits declining it. Once the follow-up is resolved or declined, ordinary legal choices are generated again from the resulting state.

The Goblin's Move After Attack is the first mandatory example. The Barbarian ability **Cleave** is the first optional example:

```text
Cleave [2/game]

After an Attack deals 2 or more damage to a Unit,
you may immediately deal 1 damage to an adjacent enemy.
```

Cleave starts each game with 2 remaining uses and a maximum of 2. After the Barbarian's Attack has fully resolved, if that Attack dealt at least 2 Damage to at least one Unit, Cleave has at least one use remaining, and at least one legal Cleave target exists, an optional Cleave follow-up is created. Damage dealt to different targets is not added together for this condition. Ordinary activation choices do not resume until the Barbarian either uses Cleave or declines that follow-up.

Each adjacent hostile Unit is a separate legal Cleave target. For Cleave, adjacent uses the same eight surrounding cells and normal Line of Sight requirement used by other adjacency rules. Choosing a target immediately spends one Cleave use and deals 1 Damage directly to that target. This Damage is not an Attack and does not roll Attack or Defence Dice. Current HP is reduced by 1, never below zero, and normal death resolution applies.

Declining Cleave spends no use. Whether used or declined, that particular follow-up then ends and ordinary legal choices are generated again. Cleave has no once-per-activation restriction of its own: if a future rule allows the Barbarian to make another Attack during the same activation and that Attack independently deals at least 2 Damage to a Unit, it may create another Cleave follow-up if a use remains.

Cleave does not require persistent activation history such as the last choice made or the maximum Damage dealt earlier in the activation. Its eligibility comes directly from the Attack that has just resolved, preserving the physical rule that Cleave is an immediate continuation rather than an opportunity that can be saved for later.

## Normal attacks

A normal Attack is a basic Action.

A Unit can make a normal Attack only when both its effective `RNG` and effective `ATK` permit it. Base stats may be modified by rules or abilities. General effective stats are derived without assuming a particular target. When an Attack targets a specific Unit, target-specific rules may further modify the values used for that combat. Attack resolution uses these authoritative target-specific attack inputs where applicable rather than treating the Unit's general effective stats as necessarily final.

The first target-specific attack modifier is the Rogue's Backstab passive. Broader categories, ordering, or a universal combat-modifier framework are deliberately deferred until concrete content requires them.

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

The attacker rolls a number of Attack Dice equal to the authoritative `ATK` for that specific Attack, including any applicable target-specific modifiers such as Backstab.

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

This value is the **Damage dealt by the Attack to that Unit**. Damage dealt is determined before applying the defender's remaining HP and is not capped by that HP. For example, an Attack that produces 2 Damage against a Unit with 1 current HP still dealt 2 Damage to that Unit.

For an Attack with multiple targets, Damage is determined separately for each target. Damage dealt to different targets is not implicitly combined into one total Damage value for the Attack.

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
- stat modifier rules beyond the currently defined additive `ModifierThisTurn(stat, amount)` behavior,
- edge effects beyond the currently defined Passable and Blocks LOS properties and the Open Door action,
- larger-than-1×1 figure behavior,
- loose tokens and markers,
- exact terrain and edge-feature taxonomies,
- scenario validation against an inventory of physical components.

These should be introduced only when the corresponding game rules or content require them.
