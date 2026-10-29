# Project Delve — Constraints

This document captures the foundational constraints for Project Delve. It is intentionally focused on the physical game and its representation, not on gameplay rules that have not yet been decided.

The digital implementation is a reference implementation of a physical board game. The physical game is the source of truth for what must be possible.

## Design principles

### Physical first

Every mechanic must be possible to perform and represent in the physical game.

The digital implementation may automate rules and calculations, but it must not introduce capabilities that only make sense because a computer is available. Randomness, state changes, positioning, and other mechanics must have a plausible physical equivalent.

The digital representation models the physical game. The physical game must not need to adapt to shortcuts or limitations in the digital implementation.

An explicitly designated **digital-only** scenario or Unit may introduce a mechanic that would be impractical to operate with physical components. Such content is outside the physical-game rules requirement; it must be identified as digital-only and must not silently change the rules or constraints for ordinary physical content. Shared mechanics remain usable by physical content where practical.

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

While Lying, the Unit cannot Move, Attack, or use Actions, Bonus Actions, or Free Actions, and its Behavior supplies no choices. Continuous effects provided by that Unit, such as a Passive stat modifier, are inactive while it is Lying. Triggered consequences and replacement effects are not suppressed merely because their source Unit is Lying; if Posture matters to such a rule, the rule states the required Posture explicitly. Rules originating from other Units or from the scenario may still affect the Lying Unit normally. Merely being Lying does not cause the figure to stop counting as a Unit for occupancy, friendliness/hostility, adjacency, targeting, or conditions on another Unit's rules.

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

## Playtest scenarios

The reference implementation provides a small catalog of named **playtest scenarios**. These are development content for exercising rules and interactions, not a campaign, progression system, or statement that their layouts and balance are final game rules.

A playtest scenario supplies the authoritative initial setup needed to create a fresh game, including its board and initial Units. The scenario catalog exposes stable identity and human-readable presentation such as an id, name, and short description. Exact terrain layouts, Unit positions, and balancing of these playtest scenarios are implementation content and may be iterated without changing the core rules constraints.

A client may list the available scenarios and may at any time start a selected scenario, restart the current scenario, or switch to another scenario. Starting or restarting creates a fresh authoritative game state from that scenario's initial setup. The previous game state is discarded; changing scenarios does not require an in-game transition, undo operation, state migration, or completion of the current activation/resolution.

The initial catalog should contain six progressively broader playtest scenarios:

1. An approximately `8x8` scenario with Barbarian and several Grunts in a relatively open environment, emphasizing basic movement and combat.
2. An approximately `10x10` scenario with Barbarian and Rogue against Grunts and Goblins.
3. An approximately `12x12` scenario with Barbarian and Rogue against Grunts, Goblins, and Skeleton Archers, providing useful ranged and Line-of-Sight situations.
4. An approximately `15x15` scenario with Barbarian, Rogue, and Wizard against Goblins, Skeleton Archers, and Zombies, including useful Doors and terrain for their established mechanics.
5. An approximately `15x15` scenario with Barbarian, Rogue, Wizard, and Cleric against Goblins, Skeleton Archers, Zombies, Trolls, and optionally some Grunts. It should contain substantially more Walls and Doors while avoiding excessive one-cell-wide corridors and bottlenecks.
6. An approximately `15x15` Shaman-focused scenario with all four Heroes, at least one Shaman, and optionally some simpler non-Goblin enemies. Its layout should provide multiple paths and enough maze-like structure for Flee to matter while still allowing the Shaman to be pursued. **No Goblins are present in its initial setup**; Goblins should enter play through Summon Goblin so Unit creation, Lying Posture, and next-round Activation Bag participation can be exercised naturally.

These scenarios are for interactive playtesting. They use the default Side objectives and game-end rules defined below unless a scenario explicitly assigns a different objective to a Side. Campaign progression, unlocking, difficulty metadata, scoring, and additional objective types remain deferred until concrete game content requires them.

## Scenario objectives and game completion

A scenario defines an **Objective** independently for each **Side**. If the scenario does not explicitly specify an Objective for a Side, that Side uses **Eliminate Enemies**. A scenario may explicitly set **No Objective** for a Side. No Objective is never achieved; that Side cannot win or cause game completion through its own Objective, but its Units still participate normally in activation and gameplay. Objectives belong to Sides, not to the Hero/Monster distinction, individual Units, or Decision Providers. An Objective is authoritative game content: clients and automated controllers may use it to plan, but do not decide for themselves whether it has been achieved.

A **neutral Side** has no hostile relationships in either direction with the other Sides. Neutrality affects relationships, not activation or action eligibility unrelated to hostility. A neutral Side may be assigned No Objective to make it a non-winning world participant, or a concrete Objective if a scenario specifically wants that Side to be able to win. Neutrality alone does not imply No Objective, and No Objective alone does not imply neutrality.

**Eliminate Enemies** is achieved when no living hostile Units remain on the board for that Side. Use only the evaluating Side's actual hostile relationships and in-play Units when evaluating this condition; neutral, non-hostile Units do not count as enemies and never prevent completion. a defeated or removed Unit does not count. Do not predict Units that might be created later. This first Objective type needs no target selection, counters, or additional persistent per-Unit state.

### Reach Area and Defeat Units Objectives

Two additional concrete Objective kinds are available for scenarios. They reuse a narrow, shared way of identifying Units; they do not introduce arbitrary filters, nested expressions, a generic objective scripting language, or new evaluation times.

**Unit selectors** are a nonempty list of one or more of:

- **Unit (Unit ID):** one specific Unit identified by its stable canonical ID, including if later defeated. It never silently retargets another Unit.
- **Side At Start (Side ID):** the specific Unit IDs belonging to that Side in the scenario's initial Game State, captured at setup. Units created later are not included.
- **Group (Unit Type + Side):** Units belonging to that combination, identified using the existing Activation Token identity. This selector is dynamic: matching Units created later are included.

Combine selectors as a **set union** of matching Unit IDs: a Unit matched by multiple selectors counts only once. Validate authored references at scenario creation rather than silently ignoring mistakes. No generic current-Side selector or custom Unit filter is defined in this first version.

Each Objective specifies **Count**: a positive integer, or **All**, represented by `count = 0` in scenario data. Positive Count requires at least that many qualifying Units. All requires every Unit selected by the Objective to qualify. An empty selected set **never** satisfies either Objective, including All.

**Reach Area** — `ReachArea(count, selectors, x, y, w, h)`: the rectangular area consists of Cells with `x <= cell.X < x + w` and `y <= cell.Y < y + h`, with positive width and height and entirely on the board. At each established Objective evaluation boundary, a selected living Unit qualifies only if **every Cell occupied by every one of its figures** is inside that area. Partly overlapping the rectangle is insufficient. Future Units represented by multiple figures must have all of their occupied Cells inside it. Units must be present **simultaneously** at the evaluation boundary; there is no running tally of Units that previously visited the area, no automatic removal at the exit, and no early success merely for passing through during a Move. A Unit that is defeated remains in a frozen Side At Start selection but cannot qualify.

For Reach Area, a dynamic Group selector covers its **currently living in-play members**; defeated former members no longer belong to that selector. Specific Unit and Side At Start selectors retain their initially designated IDs even after defeat. Consequently `ReachArea(All, SideAtStart(Blue), ...)` cannot succeed once a required initial Unit has been defeated, but dynamically spawned group members can satisfy a Group objective.

**Defeat Units** — `DefeatUnits(count, selectors)`: at an established Objective evaluation boundary, count selected Units that were **defeated** according to the actual rules outcome. A defeated Unit retains its canonical identity, so the criterion does not mean merely that its figure is currently absent, and an effect that prevents defeat does not qualify. For a dynamic Group selector, the selector includes matching Units created since scenario start and retains previously defeated members for this Objective's counting purposes. For Side At Start, only its frozen initial IDs count. `count = 0` requires defeat of every selected Unit and a nonempty selection. No generic impossibility detector, automatic victory for the opposing Side, or cumulative Reach Area tracking is introduced.

The two Objectives use the **same existing stable evaluation boundaries** as Eliminate Enemies, including after a completed Unit activation and fully resolved Round Start World Effects. A Unit merely standing in the region mid-activation does not satisfy Reach Area until such a boundary. All Sides are evaluated against the same state, preserving the existing rule for simultaneous achievement.

**First concrete scenario — The Breakthrough (Genombrottet):** Blue aims to get all initially present Blue Heroes into an exit area simultaneously through a Reach Area Objective. Red aims to defeat any one of those initial Heroes through a Defeat Units Objective. The intended layout offers a shorter dangerous route and a longer initially safer route, with intervening Doors. These objectives and the contrasting routes express the scenario's design intent; the authored scenario definition owns its current setup. Exact map dimensions, exit-area coordinates, Unit placements and counts, Objective parameters, and initial Standing Orders are editable playtest content, not fixed game rules. Tests using real scenarios should check engine validity and successful loading/instantiation; dedicated test-owned fixtures test mechanics, serialization, and web behavior. No general trigger framework or campaign-progression system is required.

Evaluate all Sides' Objectives against the same authoritative state at **completed Unit activation boundaries** and after other fully resolved, stable game transitions capable of removing or creating Units, including completed Round Start World Effects. Do not evaluate during a partially resolved Action, Attack, World Effect, follow-up, or Unit activation. The game does not stop halfway through a resolving effect or activation merely because an Objective has become true.

At an evaluation boundary, if any Objective is achieved, record which Side or Sides achieved theirs, publish an authoritative game-completion occurrence, and end the game. If multiple Sides satisfy their Objectives at the same boundary, record each as having achieved its Objective without imposing an arbitrary priority or tiebreaker. The completed game permits no further Unit activations or Round Starts. Its final state, results, and prior resolution events remain available for presentation and playback.

Objective evaluation belongs to the common game rules, not to Resident or the browser. Scenario setup provides Objectives to every controller as part of the authoritative scenario information. Other Objective types and scenario-specific overrides may be introduced when their concrete rules are specified; this establishes the default and the shared evaluation/completion boundary, not a generic Objective scripting language.

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

Control/agency is assigned independently of Side and Unit Type. Side describes game relationships such as friendly and hostile; it does not imply human or automated control. Likewise, being Hero, Monster, companion, boss, or other content does not select a different activation model. All Units use the same activation, legality, Actions, Capabilities, and rules resolution regardless of which Decision Provider controls them.

Agency is authored for the participating combination of **Unit Type + Side**, which is also the identity of its Activation Token. All Units belonging to the same Unit Type on the same Side therefore share one controller. Different Unit Types on the same Side may use different controllers, and the same non-Unique Unit Type on different Sides may use different controllers. A human may therefore control a boss while ordinary Units on the same Side use automated Behavior without changing either Unit's rules.

The controller for the active Unit Type + Side group selects the next Unit within that group's activation. A Unit created during play uses the controller already assigned to its Unit Type + Side group. If that combination did not previously exist, it inherits the controller of the Unit that created it unless the creating rule explicitly specifies another controller.

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

The engine consults a Decision Provider only when there is a meaningful choice. Ordinarily, exactly one candidate with no option to do nothing is selected automatically. Zero candidates with an option to do nothing resolves automatically as no selection. One candidate plus the option to do nothing remains an external decision, as do multiple candidates. Automatic resolution continues until the next meaningful external decision or a stable terminal state.

`RollDice` is a deliberate exception to ordinary single-candidate auto-resolution. It is a mandatory continuation boundary and must remain externally observable even though it has exactly one legal candidate and cannot be declined. Engine-level forced-choice progression, choice refresh, or relevance handling must never consume or bypass a pending `RollDice` boundary.

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

A `RollDice` input is such an authorization boundary, not a choice of random outcome. Before a controller-owned dice pool is rolled, the engine exposes one mandatory `RollDice` candidate and preserves the committed resolution context required to continue. The input may carry semantic metadata needed to present the pending roll, such as the dice family, number of dice, rolling Unit, source Action, target, or door, but it never contains or permits the caller to choose the result.

Submitting `RollDice` authorizes the engine to perform exactly that pending dice pool through the normal Random Provider. The engine remains the authority that invokes randomness, validates the returned faces, interprets the result, emits the resulting dice-roll event, and continues resolution. The same Random Provider abstraction remains replaceable for deterministic tests and other environments.

Dice ownership follows the Unit whose dice are being rolled. The attacking Unit's controller owns the Attack Dice roll. Each defending Unit's controller owns that Unit's Defence Dice roll. The acting Unit's controller owns a Try Open Door roll. A multi-target Attack therefore has one Attack Dice boundary followed, as resolution reaches them, by one Defence Dice boundary for each target that actually rolls Defence Dice. A zero-dice pool has no `RollDice` boundary because no physical dice are rolled.

The rules engine does not distinguish a human `RollDice` from an automated one and does not provide a second random-resolution path. An automated Decision Provider may immediately submit the same mandatory candidate. A client may likewise offer presentation preferences such as automatically submitting an exposed `RollDice` when it is the only pending input. Such auto-roll behavior is client/provider policy only: it does not bypass the boundary, supply a result, or move randomness out of the engine.

The state-machine and client boundary therefore distinguish genuine choice among alternatives from mandatory continuation boundaries, while both remain authoritative inputs required before unresolved rules resolution proceeds.

### Engine results and rules events

Each engine run starts from a stable state that is waiting for input, accepts a command or decision, and resolves completely until the next stable point that requires external input or the game ends.

The result of such a run contains, conceptually:

- the new authoritative Game State,
- a sequence of Rules/Domain Events describing what happened during the run,
- the next required input, if any.

Rules/Domain Events describe semantically meaningful gameplay occurrences and outcomes that have already been resolved by the engine. They are not commands that a client must execute in order to produce the new Game State.

The event stream should expose the meaningful occurrences needed to present play without requiring the client to infer rules from state changes. The current minimum vocabulary should include activation start and completion, Action/Bonus Action/Free Action use, Attack start with its fixed target context, each completed dice pool, and Round start, alongside the existing token draw, movement, posture, creation, attack outcome, healing, death, door, and Round completion events. Existing outcome events may remain when they already carry the appropriate rule semantics; a separate event type is not required merely to create another animation.

A completed dice pool produces a generic authoritative `DiceRolled` occurrence containing the semantic dice family, actual faces/results, owning or rolling Unit, relevant source Action and source Unit, and target or door context when applicable. Attack, Defence, and action-check rolls use this same event concept. Presentation-specific instructions such as animation duration, visual dice positions, camera movement, or `ShowDiceAnimation` do not belong in Rules/Game State or Rules/Domain Events.

Derived Passives such as Fury, Aura, and Backstab do not emit an activation event merely because their condition currently affects a rule. Explicitly using an Action, Bonus Action, or Free Action such as Rage, Focus, or Dash is a gameplay occurrence and should be represented by the generic action-use event.

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
| Asphalt | yes | no |
| Tree | no | yes |
| Solid Rock | no | yes |
| Fruit Tree | no | yes |
| Water | no | no |
| Lava | no | no |
| Gravel | yes | no |
| Stone floor | yes | no |
| Stone floor with table | no | no |

**Solid Rock** is a distinct Terrain type with the same gameplay properties and interactions as Tree: Passable = no, Blocks LOS = yes. It has no special effect of its own. Movement, placement, Phase traversal, ordinary attack LOS, and Fog of War vision treat Solid Rock exactly as Tree. It has its own terrain identity and presentation; dedicated artwork may be added separately. No new digging, destruction, or other gameplay mechanic is introduced.

**Asphalt** is ordinary, non-directional Terrain with **Passable = yes** and **Blocks LOS = no**, and no intrinsic special effects. Any future restriction to Asphalt-only traversal belongs to the Unit's movement capability, not to Asphalt itself.

**Gravel** is an ordinary Terrain type with **Passable = yes** and **Blocks LOS = no**, identical to Stone Floor in its base mechanical properties. It has no special effects, directions, railway behavior, or additional movement rules. Gravel may be authored and occupied independently of Rail; any use of gravel artwork as Rail's visual base is presentation-only and does not change either Terrain type's rules.

**Lava** is an additional Terrain type: **Passable = no**, **Blocks LOS = no**. Each Lava Cell has a scenario-authored **Coal generation** setting, defaulting to **On**. When On, it is a source of Coal at Round Start. When Off, it makes no Coal-generation rolls and produces no Coal; its appearance, lighting, movement and LOS properties are unchanged. Lava does not itself hold a Resource. This per-Cell setting is part of the scenario's physical setup information.

**Lava Coal generation:** At the start of every Round, each Lava Cell with Coal generation On rolls a **d8 twice**, resolving each roll in order. Each result designates exactly one of the eight surrounding Cells, numbered clockwise starting with **1 = north**: 1 north, 2 northeast, 3 east, 4 southeast, 5 south, 6 southwest, 7 west, 8 northwest.

For each roll, attempt to place **one Coal** on the designated Cell. Place it only if that Cell exists on the Board, is adjacent to the Lava Cell under ordinary eight-neighbor geometry **and geometric Line of Sight**, and can hold a Resource: passable terrain, no Machine, and no Resource already there. A Unit standing on that Cell does not prevent placement. If any condition fails, that roll creates no Coal. Do not reroll or select another Cell. Resolve the second roll against the updated Board, so two rolls targeting the same Cell can create at most one Coal there.

Resolve Lava Cells in top-left order (ascending y, then x); each Lava Cell makes its own two rolls. Lava generation occurs once per Round **after Round Start World Cards and all their consequences have resolved, but before Activation Bag population**. It is an automatic world operation, requires no Unit activation or Decision Provider, and does not move or overwrite existing Resources. Initial Round Start follows the same timing. Successful placements are authoritative State changes with presentation events.

There is no separate terrain-level distinction between being passable and being occupiable. Under the base rules, terrain that is passable may also be occupied; whether a particular Unit may actually end movement there is determined separately, including Unit occupancy.

Terrain tiles currently fall into two broad physical categories.

### Cell Markers and Banana Peel

A **Cell Marker** is an individually typed marker attached to one Board Cell. It is neither Terrain, a Resource, a Unit nor a Machine. Cell Markers do not replace the Cell's Terrain and do not intrinsically change Line of Sight. The initial marker type is **Banana Peel**. Under the initial rules, a Cell holds at most one Cell Marker; additional marker types and stacking behavior are deferred.

A Banana Peel can occupy an otherwise passable Cell, including one holding a loose Resource, but not a Cell occupied by a Unit or Machine when newly placed. Its presence does not block Line of Sight or change the Cell's underlying Terrain passability. A Banana Peel Cell is a legal **movement destination** but may not be traversed as an intermediate Cell on an ordinary movement path. This restriction is applied when determining legal paths/destinations, including automated behavior's ordinary movement choices; behavior does not need to evaluate or anticipate slipping.

**Planning and reachability:** Banana Peels do **not** block hypothetical terrain-route queries used to determine whether a goal can be reached, including Approach Distance, Worker resource/recipient planning and other Behavior goal-distance evaluations. Such queries ignore Cell Markers as traversal obstacles while still respecting ordinary Terrain, Edges and other relevant route constraints. This does **not** authorize an actual Move through a Banana Peel: ordinary legal movement candidates and their paths still prohibit intermediate traversal, and arriving on a Banana Peel still triggers slipping. Automated Behaviors need not predict or avoid the slip; when a goal lies beyond a Peel, they should still recognize the goal as reachable and may select a legal intermediate destination that progresses toward it, including the Peel itself when appropriate.

**Arrival trigger:** Whenever a Unit actually arrives on a Banana Peel Cell through an ordinary Move or an effect that relocates/places the Unit, immediately remove that Banana Peel, set the arriving Unit's Posture to **Lying**, and end that Unit's remaining activation if it is currently activating. The trigger is caused by arrival, not merely by starting an activation already on a marker. Emit authoritative state/presentation events for consumption and Posture changes. The marker is consumed once, before further arrival consequences can re-trigger it.

An ordinary Move remains **atomic**: path validation prevents crossing a Banana Peel Cell as an intermediate Cell, then the trigger resolves after arrival at its destination. A special sequence of individually resolved relocations, including **Run Rails**, resolves arrival after **each** successful step. If a Locomotive arrives on a Banana Peel at step 2 of Run Rails (4), it becomes Lying and the remaining rail steps and activation cease. External placement/relocation triggers the same slip; if no activation is currently underway for that Unit, it does not retroactively cancel another Unit's activation.

Banana Peels are not inherently restricted to Asphalt or any other specific passable Terrain. Placement by an ability must require an existing passable Cell with no Unit, Machine or existing Cell Marker; loose Resources do not prevent placement. An ability may impose further target requirements, but an automated Unit's preference for Asphalt is a **Behavior choice**, not a general Banana Peel rule.

### Directional railway terrain and Locomotive

**Rail** is passable terrain that does not block Line of Sight, and has one authored outgoing orthogonal direction (North, East, South or West). Ordinary Units may enter, cross, and occupy Rail Cells under their usual rules. Rail direction has no effect on their ordinary movement.

**Locomotive** is a 1 × 1 Unit Type with `MOV 0`, no Attack Action, and an automatic **Run Rails (4)** effect when its activation begins while Upright. Its Side and controller are assigned normally, including the option of a neutral Side with **No Objective**. The Locomotive requires no controller choices for its movement. A Lying Locomotive follows the ordinary Lying activation rule instead of running rails.

Run Rails consists of **up to four consecutive, separately resolved one-Cell steps**, rather than one ordinary atomic Move. At each step, read the outgoing direction of the Rail Cell the Locomotive currently occupies and attempt to move into the neighboring Cell in that direction. Every destination must also be Rail terrain. Ordinary Board boundaries, blocking Edges, terrain and placement constraints apply. If the Locomotive is not on Rail, the next Cell is not Rail, or the step is blocked, **stop the remaining sequence**. Run Rails does not spend or use the Unit's ordinary MOV; it does not activate Transport Resource. Each successful step and collision is resolved before the next step, allowing normal consequences to resolve using the updated state.

**Rail collision:** A 1 × 1 Unit on the destination Cell, regardless of Side or hostility, does not block the step:
- If the occupying Unit is Upright, the Locomotive and that Unit simultaneously **swap Cells**, and the struck Unit is then **laid down**.
- If the occupying Unit is already Lying, it is **defeated** and removed through the ordinary defeat lifecycle, then the Locomotive enters its former Cell.
- A Unit occupying any part of the destination with a footprint larger than 1 × 1 blocks the Locomotive's step; the sequence stops without moving either Unit.

Collision is **not an Attack**, uses no Attack or Defence dice, and requires no hostility. It does not cause Resources on either Cell to move: swapping/externally repositioning a Unit and these special rail steps are not ordinary voluntary Moves. Use the existing physical Posture and defeat rules and their normal consequences; do not introduce a separate collision damage/stat system. An ordinary blocking Edge prevents collision because the Locomotive cannot enter the destination.

Run Rails ends after four successful steps or the first blocked step, whichever occurs first. There is no Facing requirement, wagon, switch, signalling, schedule, or cargo mechanic in this initial railway content.

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

The boundary between two orthogonally neighboring Cells is a single shared Edge in the game model. When describing an Edge or Door relative to a Cell, rules use **bordering** rather than the defined Unit/Cell gameplay term **adjacent**.

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

An Edge exists only between two Cells on the board. The outer boundary of the board is not an Edge where a Wall or Door can be created. A Wall or Wall with window may not be placed between two Water terrain Cells. An impassable Edge cannot be placed inside the occupied footprint of a 2 × 2 figure; doing so would invalidate that figure's placement. These restrictions apply to changes during play as well as initial setup.

### Replace Edges

**Replace Edges** is a reusable operation for changing a specified set of board Edges. An effect supplies an explicit desired value for each affected Edge, using the ordinary Edge types (including **None**, which means no feature on that Edge). An Edge absent from the supplied set is not changed; an Edge explicitly assigned None is considered for removal/replacement like any other value.

For each supplied Edge independently, check whether replacing its current value with the desired value is legal under the ordinary board, terrain, feature, and figure-placement rules. If legal, replace that Edge's current value, whether it is a Wall, Door, Wall with window, or None. If illegal, leave that Edge **exactly as it was**. One failed replacement does not prevent other replacements. All replacements are evaluated against the same pre-operation board state and applied together, so input order does not affect legality or the outcome. The operation does not move figures or alter terrain.

Replace Edges describes the common operation, not a particular shape or pattern. An ability can construct a set of replacements for its own pattern without introducing a separate pattern framework.

### Doors

Doors are physically capable of being opened and closed.

Their current physical state is therefore part of Physical State.

The gameplay consequences of an open or closed door are rules and are deliberately not defined here.

### Windows and openings

Walls may contain openings such as windows.

For the currently defined **Wall with window** edge, the edge is impassable for movement and does not block Line of Sight, as specified by the edge-property table above. Additional window- or opening-specific effects beyond those properties are deliberately deferred.

## Figures

### Footprint

A figure has either a **1 × 1** or **2 × 2** footprint. These are the only supported footprint sizes.

A 1 × 1 figure occupies one Cell. A 2 × 2 figure occupies four Cells in a square. A 2 × 2 footprint has no orientation and cannot turn or rotate as a gameplay operation. Rectangular footprints such as 2 × 1, arbitrary shapes, and a general rotation/facing system are not part of the current rules.

A figure's Position is its anchor Cell. For a 1 × 1 figure the anchor is its occupied Cell. For a 2 × 2 figure the anchor is the top-left occupied Cell; if the anchor is `(x,y)`, the footprint occupies `(x,y)`, `(x+1,y)`, `(x,y+1)`, and `(x+1,y+1)`. Top-left is only a coordinate convention and does not create facing.

The footprint belongs to the Unit Type and describes one physical figure/base. A 2 × 2 Unit is still one Unit with one identity, HP value, Posture, activation, Side, controller, and set of abilities. Occupying several Cells never causes it to count several times for an effect or condition.

### Position and placement

A figure may be placed only where its complete footprint can physically and legally stand. Every occupied Cell must be on the board, normally passable for a figure, and free of other figures.

The interior of a 2 × 2 footprint may not span an Edge that is impassable for movement. For example, a 2 × 2 figure cannot stand with a Wall, Closed Door, or Wall With Window running through its base even when all four terrain Cells are individually passable. Passable internal Edges such as an Open Door do not prevent placement.

Initial scenario setup and runtime placement use the same complete-footprint legality. Two figures' footprints may not overlap.

A figure cannot occupy an intermediate position between Cells. A 2 × 2 figure physically occupying several Cells according to its footprint is not an intermediate position.

A Lying figure retains its complete footprint. Removing the figure removes the complete footprint.

A Unit with a footprint larger than 1 × 1 cannot be moved, repositioned, or have its position exchanged by another Unit's effect unless that effect explicitly says otherwise. This restriction does not prevent the Unit's ordinary Move or movement/repositioning caused by its own abilities. A rule such as `move an enemy Unit` or `swap places with an enemy Unit` therefore has only 1 × 1 Units as legal affected Units by default.

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

### Unique Unit Types

A Unit Type may be **Unique**. At most one Unit of a Unique Unit Type **per Side** may be in play at a time.

Unique is a physical tracking constraint within each Side, not a Hero, Monster, or controller distinction. A Unit Type may have maximum `HP` greater than 1 only if that Unit Type is Unique. This keeps damage physically attributable to a single identifiable figure/card instead of requiring players to distinguish several otherwise interchangeable figures of the same Unit Type with different remaining HP values.

Unique does not require maximum `HP` greater than 1; a Unique Unit Type may still have `HP 1`.

Unique constrains simultaneous in-play Units, not how many times that Unit Type may ever enter play during a game. Future rules may therefore remove and later return or recreate a Unique Unit, provided no more than one Unit of that Unit Type is in play on the same Side at the same time.

Scenario setup and any rule that creates or places Units must preserve this invariant.

### Heroes and Monsters

Hero and Monster are content classifications, not different rules or agency models. Control is assigned independently of Side and Unit Type as defined under Decisions, agency, and randomness.

Current playtest content normally presents Heroes to a player and supplies automated Behavior for Monsters, but this is a scenario/client convention rather than a fundamental distinction. Monster behavior may determine, for example:

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

### Standing group Orders

An **Order** is a standing instruction for a **Unit Type + Side** group, the same combination identified by its Activation Token. Orders are Rules/Game State and can be supplied by the scenario at setup. They are **not** a Controller, an Action, a new legality rule, or an additional activation phase. An Order applies to all Units of its group whenever an automated Behavior provider chooses among ordinary engine-supplied legal candidates. It does not restrict the choices available to a Human or other provider exercising Direct control.

A group may have at most one current Order. **No assigned Order means Normal**; there is no requirement to record an explicit Normal Order. An Order persists across Rounds until changed or removed. A newly created Unit, including a summoned Goblin, follows the current Order for its Unit Type + Side. If that combination has no Order, it follows Normal. Orders must be representable for physical play, for example on a scenario sheet or group order card; no remembered hidden persistent instructions are required.

An Order states the group's intent. Existing legality, phase progression, mandatory effects, and individual Unit capabilities remain authoritative. Behavior selects only among legal candidates and recalculates its preference from the current state at each decision. Unit-specific Behavior and preferences remain relevant where they do not contradict the effective Order. The same Order may thus be carried out differently by different Unit Types. A Unit evaluates its own capabilities but ordinarily treats other Units as standard Units; it does not assume their special abilities or intentions.

The initial Order kinds are:

- **Normal** (implicit, no assignment): use the existing Default Behavior unchanged, including the Unit Type's established special preferences and follow-ups.
- **Do Nothing**: take no voluntary actions. Stay instead of voluntarily moving, and End Turn as soon as permitted. Do not voluntarily Attack, use Actions, Bonus Actions or Free Actions, open Doors, or choose optional follow-ups. Mandatory rules outcomes, rolls, continuations, and posture changes still resolve normally; Do Nothing cannot cancel an already mandatory consequence. Unit selection and completion of a group activation still proceed.
- **Hold Position**: Stay instead of taking the ordinary voluntary Move; for Action, abilities, and other voluntary opportunities use Normal Behavior. Being moved by another effect does not create an obligation to return to the previous Cell. Hold Position does not prevent forced movement.
- **Move To (Cell)**: during the ordinary Move, choose among legal movement destinations to make as much progress as possible toward the specified Cell, using traversable route distance rather than geometric closeness through impassable terrain. Do not stop short merely to Attack an enemy passed on the way. When already at the destination, Stay. After the ordinary Move, select Attacks and abilities using Normal Behavior where available, so the Unit may Attack an enemy that happens to be in Range at the end of its Move. If no legal move makes useful progress, Stay when allowed and retain the Order for subsequent activations; do not clear it or silently substitute a different destination. Deterministic path and destination tie-breaking must be specified consistently with existing movement conventions.
- **Attack (target)**: target one specific hostile Unit by stable Unit ID, one hostile Unit Type + Side group by its Activation Token, or one hostile Side. Prefer a legal established Attack Action against an eligible ordered target; otherwise use the ordinary Move to approach a position from which such an Attack can be made, as specified below.
- **Protect (Unit ID)**: protect one specific living friendly Unit. During the ordinary Move, prefer to remain adjacent to its current footprint or approach such a position, as specified below. Actions and other choices use Normal Behavior.
- **Retreat**: during the ordinary Move, use the reusable Flee priority to increase separation from living hostile Units, as specified below. Actions and other choices use Normal Behavior.
- **Interact With (Door edge)**: target one Door by the canonical unordered pair of adjacent Cells bordering its Edge. Prefer the Unit's existing legal Door interaction or use the ordinary Move to approach a position from which that interaction can be supplied, as specified below.

Move To does **not** automatically redefine every movement-producing ability or special follow-up as another ordinary Move. In particular, Displacer Demon Swap and Goblin Move After Attack / Back Away After Attack need explicit Order-specific Behavior decisions before their priorities are changed. Until then, these choices retain their established Normal Behavior; this is a deliberately provisional interaction, not a claim that all future abilities ignore Orders. Any later change must be documented for physical play and must not change the ability's legality or effect.

An ordered specific Unit is identified by that Unit's stable `Unit ID`. A scenario may author a nonblank Unit ID for a Unit; that value becomes the runtime Unit's canonical `Id`. Authored Unit IDs must be unique within the scenario. Units without authored IDs continue to receive deterministic generated IDs, and generation must avoid every authored or already generated ID. An ID reference continues to identify the same Unit through movement and after defeat or removal and never binds to a replacement Unit. At setup, a specific Attack target must identify an initial living hostile Unit, and a Protect target must identify an initial living friendly Unit. A Unit Type + Side Attack target must name an authored agency group, and a Side Attack target must name a known hostile Side.

These references have direct physical representations: a scenario sheet or group order card records the specific figure's Unit ID, the Unit Type + Side group's Activation Token, the Side, or the Door's two endpoint Cells. Individually referenced figures must therefore be physically distinguishable by that recorded ID. The references add no hidden state.

For **Attack**, an eligible ordered target is a living, in-play hostile Unit matching the recorded scope. A specific-Unit scope never changes identity. A Unit Type + Side or Side scope is evaluated from current state each time, so it includes matching Units created later, including summons. If no eligible ordered target remains, use Normal Behavior without removing the Order. A living matching target remains eligible even when it is currently unreachable.

At an Action decision, an Attack candidate matches the Order when at least one Unit in its authoritative `TargetId` or `TargetIds` is an eligible ordered target. This includes every currently established Action that the rules classify as an Attack: Normal Attack, Claw Attack, Fire Breath, and Fireball. It does not include hostile-target Actions that are not Attacks, such as Telekinesis or Holy Wave. Fireball may affect Units outside the ordered scope as required by its normal all-Units target rule; including an eligible ordered target makes the candidate match but does not change Fireball's target set.

When selecting among **legal Attack candidates**, under either **Normal** or **Attack**, use the same reusable, deterministic ranking. Apply these successive filters; each step considers only candidates remaining after the previous step:

1. **Greatest number of eligible ordered target Units hit by this Attack.** Under Normal, or when no living ordered targets remain, every candidate receives zero. When no candidate hits an ordered target, the maximum is zero and all candidates continue to the next filter; it does not prevent an Attack on other hostile Units.
2. **Highest effective ATK for this Attack**, using authoritative Attack Dice counts rather than the Unit's printed base ATK. If a multi-target Attack has different target-specific ATK counts, use the greatest authoritative count among its targets for this ranking; this ranking does not change the counts actually rolled.
3. **Greatest number of hostile Units hit by this Attack in total.** The count of ordered target Units is already tied at this stage.
4. **Unlimited use preferred** over a limited-use Attack when otherwise tied.
5. **Earlier Action/Ability in the Unit Type's authored definition**, with ordinary Normal Attack treated according to its established declaration position; use canonical candidate ordering as a final deterministic tie-breaker among candidates of the same definition entry.

An Attack candidate's target set is the authoritative set affected by that specific candidate, not an assumed single primary target. These preferences rank Attack candidates only; they do not make an Attack legal, alter its effects, or outrank a separately established non-Attack ability preference such as Use Summon, Swap, or Displace. Where no legal Attack exists, continue to established Normal Action/ability Behavior. This generic ranking replaces any special Fire Breath-versus-Claw-versus-Normal Attack priority, including under Normal.

For Attack's ordinary Move, stay when allowed if the current position permits any established Attack Action to legally attack an eligible ordered target. Otherwise prefer a legal destination that permits one, ranked by shortest canonical movement path, then destination anchor `y`, `x`, and canonical candidate key. If no such destination exists, prefer the legal destination with the smallest remaining traversable distance to any position from which an established Attack Action could legally attack an eligible ordered target; break ties by current canonical movement-path length, destination anchor `y`, `x`, and canonical candidate key. This remaining-distance analysis ignores figures as traversal obstacles while respecting the moving Unit's traversal permissions and the existing Range, LOS, targeting, and Action rules of the Attack being considered, consistently with the established approach query. If eligible living targets exist but no destination has a reachable attack position, stay when allowed rather than treating the Order as completed or falling back to Normal movement. After the ordinary Move, and for special movement, follow-ups, abilities, and other decisions not explicitly overridden here, use Normal Behavior. Attack does not redefine established Attacks, targeting, or Unit-specific abilities.

For **Protect**, the preferred distance is ordinary eight-neighbor footprint adjacency without overlap. If the protected Unit is no longer living and in play, use Normal Behavior without removing the Order. Otherwise reevaluate its current footprint at every decision. For the ordinary Move, stay when allowed if the protecting Unit is already adjacent. Otherwise choose the legal destination with the smallest remaining traversable route to any position adjacent to the protected Unit's current footprint; break ties by current canonical movement-path length, then destination anchor `y`, `x`, and canonical candidate key. The remaining-route query ignores figures as traversal obstacles while respecting terrain, Edges, the moving Unit's traversal permissions, and both Units' footprints. If no legal destination makes progress, stay when allowed. Actions, Attacks, abilities, special movement, follow-ups, and other decisions use Normal Behavior. Protect adds no threat analysis, interception, or prediction.

For **Retreat**, the ordinary Move uses exactly the reusable generic **Flee** priority. For each legal destination and Stay when allowed, take the minimum reachable 1 × 1 terrain-route distance between a Cell in the acting Unit's footprint at that destination and a Cell occupied by any living hostile Unit, ignoring figures as route obstacles and respecting the acting Unit's traversal permissions. Prefer the greatest such minimum distance. If there are no living hostiles, no hostile has a reachable Cell pair, or no legal move is strictly safer than staying, Stay. Break remaining ties by shortest canonical movement-path length, then destination anchor `y`, `x`, and canonical candidate key. Retreat does not predict threats. Actions, abilities, special movement, follow-ups, and other decisions use Normal Behavior.

For **Interact With**, Door identity is the canonical unordered pair of its two adjacent endpoint Cells and does not include whether the Door is open or closed. At setup, the target must identify an existing Open or Closed Door Edge. While that Edge is a Closed Door, whenever the engine supplies a legal Open Door or Try Open Door candidate for that Door at the corresponding decision, choose it. Otherwise, for the ordinary Move, approach the nearest anchor position from which that Unit's existing Door interaction could legally be supplied: minimize remaining traversable route distance, then current canonical movement-path length, destination anchor `y`, `x`, and canonical candidate key. This remaining-route analysis ignores figures as traversal obstacles while respecting the Unit's movement and Door-interaction rules. Reevaluate the Door state, interaction candidates, and possible routes after every decision. This Order never supplies a new Action or bypasses existing interaction legality. If the Door is already open, the Edge is no longer a Door, the Unit has no supported Door-interaction capability, or no interaction position is terrain-reachable, use Normal Behavior without removing the Order. A Unit with no supported Door-interaction capability falls back immediately because no position exists from which it could legally perform that interaction. Abilities, special movement, follow-ups, and other decisions not explicitly overridden here also use Normal Behavior.

Canonical candidate keys are used only as a final digital determinism tie-breaker after the physically meaningful spatial priorities above. Ordered target lists, arbitrary target filters, custom priority expressions, and a general Order language are not part of these rules.

An Order may be obeyed only when its relevant choices are legal. The rules engine and automatic resolution still handle forced single choices, dice, mandatory consequences, and completion as before. Gambit's temporary Delegate control, where available in the digital client, should execute the group's effective Order-aware Behavior for the remainder of the selected Unit's activation; its advisory preview must use the same effective Behavior on copied state rather than always assuming Normal.

**Deferred Order design, not yet established gameplay rules:** ordered target lists, arbitrary target filters, custom priority expressions, and any general Order language. Future scenarios may change Orders using specified triggers, and Human or Gambit may assign hidden Orders at Round Start before World Cards in scenarios that adopt such planning rules. Warfare may additionally use locked activation sequences. None of these prospective mechanics introduces a new phase, controller, trigger system, or target grammar until its concrete rules are separately established.

### Reusable Behavior preferences under Orders — design for the next implementation

These rules refine automated choice priorities without changing legal candidates, ability effects, controller ownership, or mandatory resolution. They are specified Behavior configuration, not Unit-Type identity checks: do not implement `if Zombie`, `if Shaman`, `if RedDragon`, or `if BombImp` branches. New or existing `UnitBehavior` entries can be reused by any Unit Type that declares them.

- **Approach Through Closed Doors:** apply the Unit's existing traversal preference in *all* order-oriented path and remaining-route analysis (including Move To, Attack, Protect and Retreat), while preserving ordinary physical movement legality: a Closed Door does not become passable. A separate `TryOpenDoor` ability governs whether the Unit can attempt to open it. For available Actions, first rank relevant ordered Attacks, then use Normal Action fallback (other legal Attacks, then any legal Try Open Door); opening a nearby door does not need to help the Order. Zombie and Troll get the same behavior through their existing configuration, not through identity checks.
- **Maximize Attack Distance with Attack:** among legal Move destinations that permit an Attack on *any* eligible ordered target, choose the one maximizing distance to the **nearest legally attackable hostile Unit**, not only to an ordered target. Ties use existing movement-path length and top-left ranking. If no legal Move destination can attack an ordered target, use ordered-target approach/pathfinding. The Attack's Action selection uses the common Attack ranking.
- **Maximize Attack Distance with Protect:** among legal Move destinations that keep the protected Unit within effective RNG and normal LOS **and** permit an Attack on at least one hostile Unit, choose the one maximizing distance to the **nearest legally attackable hostile Unit**, then use normal deterministic ties. If no destination qualifies, use vanilla Protect approach: become adjacent to the protected Unit if possible, or approach it. The effective RNG is the Unit's current RNG, not a hardcoded 4. This preference can be used by any Unit Type with Maximize Attack Distance.
- **Back Away After Attack:** retains full priority for the special Move After Attack follow-up, regardless of any standing Order; ordinary Move remains Order-aware. Do Nothing prohibits voluntary Attacks and therefore ordinarily never initiates the follow-up.
- **Phase:** use the Unit's actual phasing traversal for all order-oriented route analysis while retaining normal Attack LOS and legal end-position rules.
- **Swap, Displace, Reshape Room:** keep their established special voluntary ability preferences under all Orders where such actions are permitted, even when they interfere with Move To or Hold Position. Do Nothing suppresses voluntary uses. Passives continue to apply normally.

**Flee-related Order behavior** is defined by reusable Flee-based preference rules; Shaman is their initial user:

- **Normal** and **Retreat:** ordinary established Flee.
- **Do Nothing** and **Hold Position:** their explicit Stay restrictions override Flee; Hold Position still permits regular Actions/abilities.
- **Move To:** select legal destinations minimizing remaining traversable route distance to the ordered Cell; among equally good destinations prefer the greatest `DistanceToNearestHostileFrom` under existing Flee distance semantics, then deterministic ties.
- **Protect:** if any legal destination is within a traversable distance no greater than the Unit's **effective MOV** of the protected Unit, choose among those the destination with greatest `DistanceToNearestHostileFrom`; break ties deterministically. Otherwise approach the protected Unit as under Move To, using safety to break equally good progress.
- **Interact With:** Flee continues to govern movement, even where that delays reaching the Door. If an applicable interaction Action becomes legally available, the existing Action/Order priority may use it. An otherwise impossible interaction does not make an illegal Action available.
- **Attack:** consider legal Move destinations (including Stay where legal) from which at least one living ordered target is reachable along an **open, currently traversable** route within `effective MOV × 3`. If such destinations exist, choose the one maximizing `DistanceToNearestHostileFrom`, then the established Flee deterministic ties. If none qualifies but some eligible ordered target is reachable via an open route, approach the nearest reachable ordered target as in Move To, with safety breaking equal progress. If no ordered target can be reached by open routes, use Normal Flee movement. This evaluation must not treat Closed Doors as open for a Unit that lacks the relevant traversal permission.

**Use Summon with Attack:** when a legal Summon placement exists, use the existing Summon Action. With an Attack Order and living targets, prefer the legal placement with the shortest traversable open route distance to an eligible ordered target; use normal deterministic ties. When no ordered target is reachable from a legal summon placement, retain the existing top-left summon-placement preference. This does not assign Shamans' Order to the spawned Unit: it uses the summoned Unit's own group's current Order, or Normal. Priorities between multiple different Summon Actions remain deferred until such content exists.

The common Attack-candidate ranking also applies under **Normal** to all Unit Types, replacing Red Dragon's former dedicated Fire Breath/Claw behavior. Decisions remain mechanically simple and physically followable. The remaining per-order refinements described here are new Behavior rules to implement and verify; they do not imply that existing builds already implement them.

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

- Unique: yes.
- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 4`, `DEF 3`, `HP 5`.
- Actions: Normal Attack.
- Free Actions: Open Door.

#### Rogue

- Unique: yes.
- Stats: `MOV 4`, `RNG 1` (Melee), `ATK 3`, `DEF 2`, `HP 4`.
- Actions: Normal Attack.
- Free Actions: Open Door.

#### Wizard

- Unique: yes.
- Stats: `MOV 3`, `RNG 4`, `ATK 3`, `DEF 2`, `HP 4`.
- Actions: Normal Attack; Fireball; Telekinesis.
- Bonus Actions: Focus.
- Free Actions: Open Door.

#### Cleric

- Unique: yes.
- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 4`.
- Actions: Normal Attack; Heal; Holy Wave.
- Passive abilities: Aura (adjacent friendly Units get `DEF +1`).
- Free Actions: Open Door.

### Monsters

#### Grunt

Grunt is the baseline Monster Unit Type with no special Actions, Capabilities, or Behaviors.

- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.

#### Zombie

Zombie is the first Monster Unit Type used to establish the reusable Action/Behavior composition pattern.

- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack; **Break Door**, using `TryOpenDoor(2/6)`.
- Behaviors: Approach Through Closed Doors.
- Capabilities: Transport Resource.

Zombie itself has no bespoke pathfinding or Decision Provider implementation. Its door-oriented play emerges from the reusable Approach Through Closed Doors Behavior, the reusable Try Open Door Action, shared gameplay queries, and Default Monster Behavior's normal Action priorities. Approach Through Closed Doors affects only automated decision analysis; it does not make Closed Doors traversable under the movement rules.

#### Bomb Imp

Bomb Imp is a fast, fragile melee Monster with a defeat-triggered explosion.

- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 2`, `DEF 2`, `HP 1`.
- Actions: Normal Attack.
- Capability: **Explosion**, with 1 Damage.
- Behaviors (planned, not yet implemented): `PreferHostileAdjacency`, `ProtectWithoutAdjacency`. These are reusable `UnitBehavior` preferences, not special-case logic keyed to Bomb Imp identity.

**Explosion** is a reusable defeat-triggered Capability, authored for Bomb Imp as `Explosion(damage: 1)`:

```text
Explosion

When this Unit is defeated, deal 1 Damage to every adjacent Unit.
```

Explosion applies regardless of which Side defeated Bomb Imp and affects **all** adjacent Units, including friendly Units. Adjacent uses the ordinary rules-defined adjacency (including the relevant Line of Sight requirement), not geometric proximity alone. Determine recipients from the Bomb Imp's footprint at defeat, before its figure was removed; resolve the defeat and its queued automatic consequence using the ordinary defeat and automatic-consequence rules. Each qualifying Unit is affected once. Further defeats and their consequences follow normal resolution.

**Reusable Behavior preferences (specified, not yet implemented):**

These movement preferences are selected through the Unit Type's `UnitBehavior` configuration (for example, `.WithBehaviors(UnitBehavior.PreferHostileAdjacency, UnitBehavior.ProtectWithoutAdjacency)`). Their implementations must not test whether a Unit is a Bomb Imp. Other Unit Types can use either preference independently, including Units without Explosion.

- **PreferHostileAdjacency:** for ordinary voluntary Move choices under Normal or Orders, first apply the active Order's movement ranking. Among destinations still equally preferred (including Stay when legal), choose those **adjacent to the greatest number of hostile Units**. Ignore friendly adjacency for this preference; use established deterministic tie-breaking afterward. This does not override the Order's primary ranking.
- **ProtectWithoutAdjacency:** under Protect, prefer to get **as close as possible to the protected Unit without becoming adjacent** to it, if a suitable legal destination exists. If all legal destinations are adjacent to the protected Unit, choose the closest legal destination anyway. Treat Stay as a candidate when legal. This preference overrides Protect's vanilla adjacency preference, but does not change the legality of any move. Apply PreferHostileAdjacency to remaining ties, then normal deterministic ties.
- **Do Nothing** continues to prohibit voluntary movement and actions, regardless of these preferences. Explosion remains a mandatory defeat consequence and resolves normally.
- Outside those movement preferences, the Unit follows ordinary Order-aware Behavior and retains its own Actions, Capabilities, and effects. Neither preference requires recognizing other Units' special abilities or predicting their behavior.


#### Troll

Troll is a tougher door-breaking Monster related to the Zombie.

- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 4`, `DEF 4`, `HP 1`.
- Actions: Normal Attack; **Smash Door**, using `TryOpenDoor(4/6)`.
- Capabilities: Undying.
- Behaviors: Approach Through Closed Doors.

Troll uses the same reusable Approach Through Closed Doors Behavior and Try Open Door mechanic as Zombie, but its printed ability is **Smash Door** and succeeds on 4 of the 6 die faces.

**Undying** is a defeat replacement:

```text
Undying

If this Upright Unit would be defeated,
instead set its current HP to 1 and lay it down.
```

Undying applies to defeat regardless of what rule would cause that defeat. Because it is a replacement, it is checked immediately before defeat would occur. When it applies, defeat does not occur: the Troll remains in play in the same position, its current HP becomes 1, and it becomes Lying. Consequently, rules triggered by that Unit being defeated do not trigger.

Undying does not protect a Troll that is already Lying because its own text explicitly requires the Unit to be Upright. A Lying Troll that would be defeated therefore follows the normal defeat rules.

Once laid down by Undying, the Troll follows the ordinary Posture rules. When its activation later occurs, that activation only stands it Upright and immediately ends. No separate regeneration timer, resurrection marker, remembered state, or automatic stand-up rule is introduced.

#### Red Dragon

Red Dragon is a Unique 2 × 2 Monster. Its large footprint uses the ordinary footprint rules and does not make it a separate boss category.

- Unique: yes.
- Footprint: `2 × 2`.
- Stats: `MOV 2`, `RNG 4`, `ATK 4`, `DEF 4`, `HP 8`.
- Actions: Normal Attack; Fire Breath; Claw Attack.
- Behaviors: general Attack-candidate ranking for all its Attacks; ordinary Default Behavior movement/approach. No special Fire Breath/Claw ordering.

**Fire Breath** is an Action:

```text
Fire Breath
Action

Attack all enemies within RNG.
```

Fire Breath targets every hostile Unit that is a legal target under the ordinary ranged Range and Unit-origin Line of Sight rules. For a multi-Cell source or target, the established same-occupied-Cell-pair Range-and-LOS rule applies. Fire Breath is one Attack with multiple targets, so it uses the normal shared Attack Dice roll: roll the Red Dragon's authoritative Attack Dice once and use that Hit result against every target, while each target rolls its own snapshotted Defence Dice and resolves its own Damage.

Fire Breath has no usage limit or additional area shape. It attacks Units directly; it does not select an explosion Cell and does not use Fireball's explosion rules.

**Claw Attack** is an Action:

```text
Claw Attack
Action

Attack one adjacent enemy with ATK +1.
```

A legal Claw Attack target is one adjacent hostile Unit under the ordinary footprint-aware adjacency rule. Claw Attack is an Attack and uses the Red Dragon's authoritative `ATK` with an additional `+1 ATK` for that Attack. With the current base stats it therefore normally rolls 5 Attack Dice. It otherwise uses ordinary single-target Attack resolution.

Automated Red Dragon play uses the common Attack-candidate ranking defined under Standing group Orders, under both Normal and Attack Orders. Its movement and approach use the ordinary Behavior unless the Order changes them. This intentionally replaces the former preference for Fire Breath against two or more targets, even where the resulting Action choice differs.

#### Labyrinth Shaper

Labyrinth Shaper is a **digital-only**, Unique 1 × 1 Monster. Its room-rebuilding ability is not required to be practical on the physical tabletop and does not relax the rules for ordinary physical-game content.

- Unique: yes.
- Footprint: `1 × 1`.
- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 2`, `DEF 2`, `HP 5`.
- Actions: Normal Attack.
- Bonus Actions: Reshape Room.
- Behaviors: prefer Reshape Room at the start of its activation when available, then use ordinary Default Monster Behavior for movement and Action.

**Reshape Room** is an unlimited-use Bonus Action:

```text
Reshape Room
Bonus Action

Replace Edges in the 3 × 3 area centered on this Unit to form a room with an opening at the middle of each of its four sides.
```

The desired pattern has **Walls** along the four sides of the 3 × 3 area except for the central Edge on each side, which is **None**. Every Edge strictly inside that area's perimeter is also **None**. All those perimeter and interior Edges are explicitly included in the replacements, even if their desired value is None, so old walls and doors are cleared where legal. The room's openings are open gaps, not Doors. Edges outside the defined area remain unchanged.

If the 3 × 3 area reaches beyond the board, clip the requested replacements to existing Edges between on-board Cells. No feature is created on the board's outer boundary. Resolve the requested replacements through the ordinary Replace Edges operation: a forbidden Wall (for example, between Water Cells or through a 2 × 2 figure) is not created, and that Edge retains its previous value; other replacements still proceed.

Reshape Room neither moves Units nor changes terrain, does not require a target choice, and has no per-game use limit or cooldown. It follows the ordinary rule that each individual Bonus Action ability may be used at most once per Unit activation. For automated play, Labyrinth Shaper prefers using Reshape Room before moving, when it can act; this is a Behavior preference, not a separate timing restriction.

#### Displacer Demon

Displacer Demon is a positional-control Monster intended to disrupt defensive formations rather than merely attack through them.

- Stats: `MOV 3`, `RNG 1` (Melee), `ATK 4`, `DEF 4`, `HP 1`.
- Actions: Normal Attack.
- Bonus Actions: Swap; Displace.
- Passive abilities: Aura (adjacent friendly Units get `DEF +1`).
- Behaviors: prioritize Swap when legal; otherwise use ordinary movement; after movement and any Swap, prefer Normal Attack; after the Action opportunity is resolved, prefer Displace before End Turn.

**Swap** is an unlimited-use Bonus Action:

```text
Swap
Bonus Action

Swap places with an adjacent enemy Unit.
```

Swap uses the general swap-places rule. The enemy must be adjacent under the ordinary footprint-aware adjacency rule and must be a legal Unit for external repositioning. Because Displacer Demon is 1 × 1 and Units larger than 1 × 1 cannot normally be repositioned by another Unit's effect, Swap currently exchanges the Cells of Displacer Demon and one adjacent 1 × 1 enemy. The exchange is simultaneous and neither Unit performs a Move.

**Displace** is an unlimited-use Bonus Action:

```text
Displace
Bonus Action

Move an adjacent enemy Unit up to 1 Cell.
```

The chosen enemy must be adjacent when Displace begins and must be a legal Unit for external movement. The Displacer Demon's controller chooses 0 or 1 Cell as permitted by `up to 1`; when moving 1 Cell, it chooses a legal neighboring destination under the external-movement rules. Displace does not spend the affected Unit's `MOV` and is not that Unit's Move. It cannot normally move a Unit whose footprint is larger than 1 × 1.

Swap and Displace have no per-game use limit, but each is still an individual Bonus Action and therefore may be used at most once during each Displacer Demon activation.

Displacer Demon's **Aura** uses the same passive mechanic and adjacency semantics as Cleric's Aura: each adjacent friendly Unit gets `DEF +1`, the source does not affect itself, multiple applicable sources stack, and a Lying source does not provide the continuous effect.

For automated play, choice priority is intended to create the positional tactic from ordinary legal candidates rather than special movement legality. If Swap is legal and has not been used this activation, prefer Swap over the ordinary Move choice. If Swap is not legal, use ordinary movement Behavior. After movement, if Swap has become legal and remains unused, prefer it before choosing the Action. Prefer a legal Normal Attack for the Action. Once the Action opportunity is resolved, if Displace is legal and unused, prefer Displace before End Turn. Swap target choices use authoritative legal candidates and the normal deterministic Behavior ranking. For Displace, rank each legal target-and-destination combination by how much that displacement increases that target's distance from the Displacer Demon, and prefer the greatest increase. If multiple Displace candidates have the same distance increase, prefer the candidate that moves the target the fewest Cells. Resolve any remaining tie using the normal deterministic Behavior ordering, ranking target before destination. These Behavior preferences do not change Swap, Displace, movement, Attack, or adjacency legality.

#### Ghost

Ghost is a phasing Monster that uses the normal Monster Behavior but treats the board differently for movement and movement-path analysis.

- Stats: `MOV 2`, `RNG 1` (Melee), `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.
- Capabilities: Phase.

**Phase** changes traversal for this Unit's movement and movement-path analysis. While determining where the Ghost can move or how it can reach future positions, treat all terrain Cells and all Edges as passable. For movement purposes, the map therefore behaves like an unobstructed floor grid except for Units.

Units continue to affect movement normally. Friendly Units may be passed through but cannot be the destination. Hostile Units cannot be passed through or used as the destination.

Phase changes traversal only. A Ghost must end every Move with its complete footprint in an unoccupied placement that is normally legal for a figure. It cannot end with any occupied Cell in otherwise impassable terrain or fixed-object space, or with its footprint spanning a normally impassable internal Edge. During traversal it may pass through such terrain and normally impassable Edges, subject to the ordinary Unit-occupancy rules for its complete footprint.

Phase also applies when Default Monster Behavior evaluates reachable attack positions and remaining approach distance for the Ghost. Those analyses use the Ghost's actual phasing traversal rather than ordinary terrain/edge passability. This may make a route through otherwise impassable Cells or Edges the shortest route to a Cell from which a legal Attack can be made, or may make a longer movement route preferable because it leads toward such an attack position.

Phase does not alter Range, attack Line of Sight, targeting, or Attack rules. A Ghost's Normal Attack requires ordinary legal Range and normal Unit-origin Line of Sight. Terrain and Edges therefore continue to block its attacks according to their ordinary LOS properties even though the Ghost may move through them. The digital-only Fog of War vision exception below applies only to exploration.

#### Shaman

Shaman is a non-attacking Monster that creates Goblins and tries to keep away from reachable enemies.

- Stats: `MOV 2`, `RNG 0`, `ATK 0`, `DEF 3`, `HP 1`.
- Actions: Summon Goblin.
- Behaviors: Flee; use Summon Goblin when possible, choosing the top-left legal placement Cell.
- Shaman does not have the Normal Attack Action.

**Summon Goblin** is an Action:

```text
Summon Goblin
Action

Place one Lying Goblin in an adjacent empty Cell.
```

A legal summoned placement uses ordinary footprint adjacency and normal Line of Sight rules. The summoned Unit's complete footprint must satisfy ordinary placement legality. For the current 1 × 1 Goblin this is the same adjacent empty-Cell rule printed above. A future 2 × 2 summoned Unit would likewise require only one qualifying adjacency relationship, but its complete 2 × 2 footprint would have to be legal. Summon Goblin does not create a separate placement model or allow placement onto blocking/non-passable terrain.

The created Unit is an ordinary Goblin in all respects except that its initial Posture is Lying. It has the same Side as the summoning Shaman, its normal maximum and current HP, and all normal Goblin content. Unit Type does not determine allegiance. No spawned, summoning-sickness, or temporary state is added.

Summoned Units follow the established round-bag snapshot rule. Summoning never adds a Unit Type token to the current round's already-established bag. If a Goblin token is already present and has not yet been drawn, a newly summoned Goblin is an ordinary living Goblin when that Unit Type later activates and therefore participates in that activation. If the Goblin token has already resolved, the new Goblin waits until a later Goblin activation, normally in the next round. Creating the first Goblin during a round does not add a Goblin token to that round's bag; if at least one Goblin is alive when the next round's bag is established, Goblin participates normally.

A summoned Goblin that is still Lying when its activation occurs follows the ordinary Posture rule: it stands Upright and that individual activation immediately completes. Eligibility semantics for future content that creates a Unit while that same Unit Type token is already being resolved are deliberately deferred until such content exists.

For automated Shaman play, if at least one legal Summon Goblin placement exists when its Action is available, use Summon Goblin and choose the top-left legal Cell (ascending `y`, then `x`). If no legal placement exists, it takes no Action. This is a Behavior preference among authoritative legal choices, not additional Summon Goblin legality.

**Flee** is Shaman's ordinary-Move Behavior. Legal movement destinations are generated using the fleeing Unit's real movement rules and complete footprint. A 2 × 2 Unit using Flee therefore considers only destinations and movement paths through which its complete footprint can actually travel.

Flee's **distance ranking** is intentionally a different query. For each legal destination, consider every Cell in the fleeing Unit's footprint at that destination and every occupied Cell of every hostile Unit. Measure ordinary terrain-route distance between those Cell pairs as a 1 × 1 Cell-to-Cell path calculation: the distance path itself does not need to accommodate the fleeing Unit's footprint or the hostile Unit's footprint. The destination's Flee value is the smallest reachable such Cell-pair distance. Prefer the legal destination with the highest Flee value.

Only hostiles for which at least one such Cell-to-Cell terrain route exists participate in the ranking. The distance analysis uses the traversal parameters appropriate to Flee; in particular, Shaman does not have Approach Through Closed Doors, so Closed Doors are not made traversable merely for this calculation. If no hostile Unit has a reachable Cell pair, the Unit stays.

Ties use shortest actual movement path length, with staying treated as path length 0, then top-left board order (ascending `y`, then `x`).

Flee is only a Decision Provider preference. Its 1 × 1 Cell-to-Cell distance calculation does not change actual movement legality. A 2 × 2 fleeing Unit must still fit along its own real movement path even though the ranking distance is measured independently between footprint Cells.

Shaman introduces Unit creation during play as concrete content. Implement only the placement and Unit-creation behavior required by Summon Goblin; this does not establish a general summoning, reinforcement, spawn-wave, or Unit-factory framework.

#### Skeleton Archer

Skeleton Archer is a ranged Monster Unit Type whose automated Behavior prefers to attack while keeping as much distance as possible.

- Stats: `MOV 3`, `RNG 4`, `ATK 3`, `DEF 3`, `HP 1`.
- Actions: Normal Attack.
- Behaviors: Maximize Attack Distance.

During Move, **Maximize Attack Distance** considers every legal movement destination plus staying in the current cell when no movement is permitted as a choice. If one or more of those positions allow the Unit to make a legal Normal Attack, only those positions participate in this Behavior's preferred-position ranking. For each such position, measure the distance to every hostile Unit that could be legally attacked from that position and take the distance to the nearest such hostile. Choose the position that maximizes that nearest-attackable-hostile distance.

Ties use shortest actual movement path length, with staying treated as path length 0, then top-left board order (ascending `y`, then `x`).

This Behavior may therefore move a Skeleton Archer away from an enemy even when it can already attack from its current cell, but it never moves out of all legal Normal Attack positions merely to create more distance. If no legal reachable position, including staying, permits a Normal Attack, use the normal approach behavior instead.

Maximize Attack Distance is a Decision Provider preference, not an attack or movement rule. It uses shared gameplay queries for attack legality, distance, Line of Sight, and related calculations rather than reproducing those rules inside the provider.

### Delve Chess — first Unit Types and movement rules

**Delve Chess** is a chess-*inspired* content experiment, not a chess rules variant. A later 8×8 scenario may alternate Grass and Stone Floor to evoke a chessboard, but ordinary Delve combat, Activation Bag, Side relationships, Unit occupancy, friendly pass-through, LOS, Posture and Objectives remain authoritative. No check/checkmate, chess captures, pawn promotion, castling or en passant. The introductory scenario objective is to defeat the enemy King; the complete scenario and later pieces are specified in the sections below.

**Two visually distinct Unit Types per role:** the Hero Court has professionally equipped fantasy characters; the Monster Court has Goblins/Trolls in earnest homemade chess costumes. The counterpart types have identical Stats, Capabilities and Behavior but different IDs, names and portraits. Their names do not restrict which Side they can be assigned to. The first two pairs are:

| Role | Hero Court Unit Type | Monster Court Unit Type | MOV | RNG | ATK | DEF | HP |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Peasant | Royal Peasant | Goblin Peasant | 1 | 1 | 2 | 2 | 1 |
| Rook | Castle Guard | Tower Troll | 16 | 1 | 3 | 4 | 1 |

All four use ordinary Normal Attack. **Peasant** has ordinary movement (no special Movement Pattern), no new special Action and the default automated behavior except for **Prefer Lowest Effective Defense**: *when choosing among already-legal targets for a normal Attack*, first rank targets by their effective DEF at that decision point, ascending, then apply the existing deterministic attack-target tie breakers. It does not change Move decisions, target legality, attacks by other abilities, or any other Behavior priorities. Implement this as a reusable behavior preference.

**Rook** has **Straight Orthogonal Movement** and **Advance On Kill**. Its default automated Behavior otherwise follows the ordinary combat behavior.

**Straight Orthogonal Movement** is a reusable movement restriction: each ordinary Move may follow only a single uninterrupted cardinal direction (north, east, south or west) for at most the Unit's effective MOV; Stay is allowed. All traversed cells and edges must be legal under the existing movement rules, including friendly pass-through, hostile blocking, terrain, Walls, Windows, Doors, Resources and Cell Markers. No turn is allowed *within the same Move*, even if the endpoint would be reachable by a shorter ordinary route. A separate Move opportunity may choose a new direction. This restriction affects ordinary Move legality and destination/path generation, not attack range, LOS, or special repositioning unless explicitly stated. Existing canonical movement and deterministic choice conventions remain applicable.

**Advance On Kill** is a reusable Capability related to `MoveAfterAttack`, but it is a **conditional post-Action Move**, not an ordinary Move opportunity. After a Unit's **normal melee Attack** has fully resolved and actually defeated at least one hostile target, the attacker receives one immediate special Move with a maximum allowance of **2 orthogonal steps**. Its legal destinations are **Stay** and the former cells of hostile Units just defeated by that Attack that were eight-cell adjacent to the attacker when the Attack resolved. If more than one qualifying defeated cell exists, the player/provider chooses one legal destination. This does not apply to Fireball, Shoot Shell or other special attacks, nor when defeat was prevented by Undying, Indestructible or another effect.

For an orthogonally adjacent former target cell, the special Move uses exactly one cardinal step. For a diagonally adjacent former target cell, it uses exactly two cardinal steps through **either** of the two intermediate cells; at least one of these two-step paths must be legal. It cannot take a longer detour. Evaluate physical passage normally: blocking terrain, Walls, Windows, Closed Doors, and hostile Units block; friendly Units can be passed as usual. The defeated Unit no longer occupies its former cell. Ignore only the attacker's ordinary MOV value and special Movement Pattern for this special Move; the Capability supplies MOV 2. Normal arrival effects, including Banana Peels, apply. Stay means declining to advance. Resolve the post-Action Move immediately before the active Unit can finish or another Unit can be selected; do not reopen ordinary Moves or normal Actions after it. If no target cell is legally reachable, Stay remains legal.

These rules do **not** introduce Trample or an automatic chess-style capture. A successful normal Attack does not normally move the attacker; only a Unit with Advance On Kill receives this option.

**Artwork originals** (prepared by the established artwork pipeline, with source files preserved unchanged):
- `assets/source/units/royal-peasant.png`
- `assets/source/units/goblin-peasant.png`
- `assets/source/units/castle-guard.png`
- `assets/source/units/tower-troll.png`

For initial verification, create a compact playtest scenario using both Peasant and Rook pairs, with a mix of open terrain, Walls, Windows and Doors to exercise straight movement and post-kill passage. Bishop and Knight are specified in the following section; Queen, King and the complete introductory board are specified after that.

### Delve Chess — Bishop and Knight (second pair)

Continue the two-court convention above: each role has **two separate canonical Unit Types**, one Hero Court portrait/name and one Monster Court portrait/name. Counterparts have identical mechanics and default Behavior, and **neither is restricted to a particular Side**.

| Role | Hero Court Unit Type | Monster Court Unit Type | MOV | RNG | ATK | DEF | HP |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Bishop | Royal Bishop | Goblin Bishop | 16 | 1 | 3 | 4 | 1 |
| Knight | Royal Knight | Hobby Knight | 3 | 1 | 3 | 4 | 1 |

All four have **Normal Attack** and ordinary default combat Behavior. **Bishops** have reusable `DiagonalCorridor` ordinary movement restriction **and** `AdvanceOnKill` (as already defined above). **Knights** have reusable `LShape` ordinary movement restriction, but no AdvanceOnKill or Rear Up/reaction ability at this stage. Do not add an attack or posture reaction when a Knight survives a hit; that design is deferred.

**DiagonalCorridor movement:** Each ordinary Move is either Stay or a single straight diagonal journey, in one of four diagonal directions. This is **not a new diagonal base step**: each advance from `(x,y)` to `(x±1,y±1)` must be implemented as exactly **two consecutive legal orthogonal steps**, either horizontal-then-vertical or vertical-then-horizontal, chosen independently for each diagonal segment. At least one of those two corner paths must be legal; if both are blocked, the Bishop cannot pass that segment. For a destination `n` diagonal cells away, the complete path must consist of `n` such segments along the **same diagonal**, with cost `2n` MOV; it may not detour away from the diagonal corridor, switch diagonal direction, or stop at the intermediate off-diagonal corner cell. Respect existing Edge, terrain, friendly pass-through, hostile blocking, and Cell Marker rules for each orthogonal step. The complete Move remains **atomic**: intermediate corner cells and intermediate diagonal cells are transit, not separate Move destinations/arrivals; however transit restrictions (including inability to pass through a Banana Peel) still apply. Only the final destination resolves ordinary Move arrival effects. When both corner routes work, choose a deterministic canonical path consistent with existing movement conventions. This movement restriction applies to **ordinary Moves including extra ordinary Move opportunities** such as Boost, but not to AdvanceOnKill's separate post-Attack Move. A Bishop may change checkerboard color via an **orthogonal AdvanceOnKill**, not via DiagonalCorridor movement.

**LShape movement:** Each ordinary Move is either Stay or **one leap** to a destination with offset `(|dx|,|dy|)=(2,1)` or `(1,2)` (eight possibilities before board bounds). The leap has nominal movement cost **3 MOV** and cannot be split into shorter moves, chained within one Move, or extended merely because effective MOV is greater than 3. If effective MOV is below 3, no LShape leap is available; Stay remains legal. A leap ignores all intermediate cells, edges, terrain and Units: no transit collision, hazard or intermediate arrival checks. The **landing Cell** must satisfy ordinary final-cell occupancy and terrain legality, including hostile Unit occupancy restrictions and any applicable landing constraints. Resolve normal arrival effects (including Banana Peel) **only on landing**. This is a special ordinary-Move path rule, **not** a general ability to teleport, attack occupied cells, or move through blocked destinations. It applies equally to extra ordinary Move opportunities; each such Move allows at most one leap. LShape does not change the Knight's normal melee attack geometry: `RNG 1` still includes diagonal adjacency under existing rules.

**Artwork originals** (preserve and process using the existing pipeline):
- `assets/source/units/royal-bishop.png`
- `assets/source/units/goblin-bishop.png`
- `assets/source/units/royal-knight.png`
- `assets/source/units/hobby-knight.png`

Extend the existing Delve Chess playtest coverage with both Bishop and Knight pairs, including corridor/corner obstructions, hostile and friendly transit, windows, diagonal AdvanceOnKill, LShape landing, and Banana Peel arrival. The full chessboard scenario, Queen and King are specified in the following section; Knight Rear Up remains deferred. No chess-specific capture, check or turn-order rules are introduced.

### Delve Chess — Queen, King and the classic-looking board

Complete the two-court content with **two separate canonical Unit Types per role**, mechanically identical within each pair and assignable to any Side:

| Role | Hero Court | Monster Court | MOV | RNG | ATK | DEF | HP | Unique |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| Queen | Royal Queen | Witch Queen | 16 | 1 | 4 | 5 | 3 | Yes, per Side |
| King | Royal King | Goblin King | 2 | 1 | 2 | 3 | 5 | Yes, per Side |

**Queen:** Normal Attack, `AdvanceOnKill`, default combat Behavior and a reusable **StraightOrthogonalOrDiagonalCorridor** ordinary-Move pattern. For each ordinary Move, choose **either** one straight cardinal path under existing `StraightOrthogonal` rules **or** one diagonal corridor path under existing `DiagonalCorridor` rules. Do not concatenate the two within one Move or permit turns. Stay is always legal. Both alternatives use their established MOV costs and physical passage rules. The union applies to extra ordinary Moves as well; `AdvanceOnKill` retains its existing special post-Attack path rules and ignores the ordinary movement restriction. No new Queen-only movement engine.

**King:** Normal Attack, default combat Behavior, and the **same adjacent-friendly +1 effective DEF aura** as the existing Cleric, with identical adjacency, active-while-Upright, stacking and modifier semantics. No Fireball or other spell. Use reusable **Adjacent** ordinary-Move restriction: Stay or finish on any of the eight adjacent Cells; cardinal destinations require one legal orthogonal step, diagonal destinations require exactly two legal orthogonal steps via at least one of the two intermediate Cells, with the existing terrain, Edge and Unit passage rules. No longer detours. Its `MOV 2` supports diagonal adjacency but does not permit two-cell straight movement. Effective MOV below the needed step cost makes that destination unavailable. Additional ordinary Move opportunities each apply Adjacent independently. Ordinary melee attack geometry remains unchanged. The King has no `AdvanceOnKill`.

**Artwork originals:** `assets/source/units/royal-queen.png`, `assets/source/units/witch-queen.png`, `assets/source/units/royal-king.png`, `assets/source/units/goblin-king.png`. Preserve originals and integrate using the established artwork pipeline and fallback conventions. Hero Court uses real royal equipment; Monster Court uses earnest homemade costumes. The two sides' counterpart gameplay definitions must remain equal.

**Scenario — Chess:** Create a distinct introductory 8×8 board that looks like an ordinary chess setup at first glance. Use **alternating Stone Floor and Asphalt** Cells (not Grass) for a strong light/dark checkerboard pattern. All 64 Cells are passable and otherwise ordinary; do not attach Asphalt-only movement or any racing rule to the board. The classical layout is 16 Units per Side: on each back rank, from left to right, **Rook, Knight, Bishop, Queen, King, Bishop, Knight, Rook**, with eight Peasants on the rank in front. Opposite Side mirrors the same file order from the viewer's perspective, so the Queens occupy the same file and Kings occupy the same file. Use Hero Court types on one Side and Monster Court types on the other. Orient the board so each Side has two ranks facing the other across four empty ranks. All Units are ordinary Delve Units and use existing activation, combat, occupancy, LOS, movement, posture and abilities. No special chess capture, check/checkmate, pawn promotion, castling, en passant, or chess turn order. No railway, Karts, Banana Peels, machines, special terrain, hazards or other surprise props in this **introductory** scenario. The intended surprise is entirely in how ordinary Delve rules operate on a familiar-looking board.

**Victory objective:** Defeat the **opposing King Unit Type**, not all enemies or all Unique Units. The game ends with victory for the Side whose enemy King has actually been defeated. Reuse the existing objective framework and existing defeat semantics; do not introduce special chess check rules. Both Sides should have symmetric objectives. If the existing objective expression cannot select a specific hostile King group correctly, use the smallest generic objective extension rather than a scenario-specific hardcode, and raise an ambiguity if the rules cannot determine a symmetric result.

Preserve existing Phase 1 and Phase 2 playtest scenarios as separate mechanical test content. Add tests for Queen's union-of-patterns movement, King's adjacent path legality and aura reuse, counterpart parity, 8×8 alternating Stone/Asphalt layout, 16-per-Side classical deployment, and King-defeat victory. The unrelated future **Chess Room** dungeon ambush and Knight Rear Up reaction remain speculative and out of scope.

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

An Activation Token identifies a **Unit Type + Side** combination, not an individual Unit instance.

At the start of a Round, the default rule is to place one Activation Token in the bag for each distinct Unit Type + Side combination that currently has at least one Unit in play.

Each Hero is a Unique Unit Type and therefore normally has its own personal Activation Token. Monsters are usually grouped by Unit Type and Side. Multiple Units of the same Monster type on the same Side share one Activation Token.

The abstract rules and digital engine may represent the same non-Unique Unit Type on more than one Side. Those combinations have separate Activation Tokens and activate separately. Whether the currently available physical component set contains enough distinguishable tokens and figures to construct such a scenario is a physical scenario-construction constraint, not a universal game rule.

One token per participating Unit Type + Side combination is the default rather than a structural limit. Abilities and special rules may explicitly modify how many Activation Tokens a combination contributes.

Tokens are drawn randomly from the bag without replacement. When a token is drawn, the Unit Type + Side combination identified by that token activates.

When the bag is empty, the Round ends. The bag is then populated again for the next Round from the Unit Type + Side combinations that are still in play, applying any rules that modify their number of Activation Tokens.

The Activation Bag is snapshotted at Round start. If all Units belonging to a token leave play after that token has already been placed in the bag, the token remains. If later drawn, its activation is valid but affects zero Units. Creating, summoning, removing, or killing Units during a Round does not retroactively add or remove bag tokens unless a rule explicitly says so.

A Unit entering play during an ongoing Round may participate later that Round only if the matching Unit Type + Side token is already present and has not yet resolved. A token for the same Unit Type on another Side does not make it eligible. Future rules may explicitly modify bag contents.

### Unit Type activation

When a Unit Type + Side Activation Token activates, all living Units matching **both** that Unit Type and that Side participate in that token activation.

Units activate **one at a time**. One Unit completes its entire activation before another matching Unit is selected. Effects and physical changes produced by an earlier Unit are therefore already part of the board state when the next Unit is selected and activated.

The order is not fixed at the beginning of the token activation. Before each Unit activation, the engine determines the matching Units that are currently eligible and have not already completed an activation for this token. This concrete set is the legal decision space for choosing the next Unit.

All Units matching one Unit Type + Side token share one controller. That controller selects the next Unit from the eligible set. Default automated Behavior chooses the currently topmost eligible Unit, breaking ties by choosing the leftmost one, using current board positions. A human or future smarter AI provider may choose a different eligible Unit when the rules permit it. Once a Unit has completed its activation for the current token, it cannot be selected again for that token.

This ordering is intentionally state-dependent. For example, one Zombie may open a door and a later Zombie of the same Unit Type and Side may then move through that now-open door. Default automated Behavior is intended to remain simple, deterministic, and manually followable in physical solo play rather than planning the complete group activation. A digital-only smarter provider may plan across several legal choices without changing the underlying game rules.

Units created before their matching Unit Type + Side token is drawn participate normally when that token later resolves. Eligibility for future content that creates a Unit while its own matching token is already being resolved is deliberately deferred until such content exists.

## Unit activation

Heroes and Monsters use the same activation rules.

A Unit activation is driven by what the Unit has already done rather than by mandatory Bonus Action, Move, and Act phases. Conceptually the activation tracks at least whether the Unit has completed its Move, whether it has completed its Action, and which individual Bonus Action abilities it has used during this activation.

A living Unit normally has:

- one Move opportunity (subject to Move opportunity modifiers),
- one Action opportunity after its Move has been completed,
- each applicable Bonus Action ability at most once during the activation, subject to that ability's own rules and remaining uses,
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

Heal one adjacent damaged friendly Unit
for 2 HP.
```

Heal starts each game with 2 remaining uses and a maximum of 2. It uses the Unit's normal Action opportunity, so using Heal consumes the Cleric's Action for that activation just as a normal Attack would.

A legal Heal target is a friendly Unit other than the Cleric that is adjacent under the same eight-surrounding-cells plus normal Line of Sight rule used by Aura, and whose current HP is below its maximum HP. A Unit at maximum HP is not a legal Heal target. Because the Cleric is not adjacent to itself, Heal cannot target the Cleric.

Resolving Heal immediately spends one use and restores 2 current HP to the chosen Unit, capped by that Unit's maximum HP. The amount is not chosen: a Unit missing only 1 HP restores 1 HP because current HP cannot exceed maximum HP. Heal involves no dice roll.

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

The player chooses one target Cell using an occupied Cell of the Wizard as the source. One and the same source Cell must place the target Cell within the Wizard's effective `RNG` by the ordinary ranged Manhattan-distance rule and have normal Unit-origin Line of Sight to that Cell. Range through one part of a multi-Cell source cannot be combined with LOS from another part. Line of Sight includes blocking by intervening hostile Units. If the chosen target Cell is occupied by a hostile multi-Cell Unit, that entire Unit is excluded as a blocker to its own target Cell. The target Cell may be empty, and Fireball remains legal even when its explosion would affect no Unit.

The explosion area is the target Cell and its eight surrounding Cells. A Unit is a Fireball target when at least one **same occupied Cell** of that Unit both lies in the explosion area and has geometric board Line of Sight from the target Cell. Area membership through one Cell cannot be combined with explosion LOS to another Cell. This explosion-origin check considers blocking terrain and edges but not intervening Unit figures; the target Cell is not itself a Unit with friendly or hostile relationships. Blocking terrain or edges may therefore protect part or all of a multi-Cell Unit from the explosion.

Fireball targets **all Units** satisfying that area and Line of Sight rule exactly once, regardless of how many of their occupied Cells qualify. Friendly fire therefore applies. The Wizard may also be a target of its own Fireball if at least one of its occupied Cells satisfies the same rule.

Fireball is one Attack with multiple targets. Its Attack Dice use the attacker's general effective `ATK` when the Fireball begins, so ordinary this-turn modifiers such as Focus affect Fireball. The Attack Dice are rolled once for the whole Fireball and the resulting Hits are shared by every target. Target-specific attack modifiers do not independently change that shared roll unless a future rule explicitly says they apply to such an Attack.

Each target rolls its own effective `DEF` separately. Damage is calculated and applied separately to each target using the normal `max(0, Hits - Blocks)` rule, and normal death resolution applies. The complete target set is determined when the Attack begins, before Attack Dice, Defence Dice, Damage, or deaths are resolved.

Rules that occur after an Attack occur once after the complete Fireball has resolved, not once per target. Damage dealt to different targets remains separate for rules such as Cleave; Damage is not added across targets.

Fireball is the current concrete shared-roll multi-target Attack. This does not establish a universal area-effect or spell framework. Holy Wave is a Posture-changing Action, not an Attack.

The Wizard also has the Action ability **Telekinesis**:

```text
Telekinesis
Action

Lay down an upright enemy within RNG and LOS.
```

Telekinesis has no per-game usage limit. It uses the Unit's normal Action opportunity and therefore competes with the Wizard's normal Attack and Fireball.

A legal Telekinesis target is an Upright hostile Unit for which at least one occupied-Cell pair simultaneously satisfies the Wizard's effective `RNG` and normal Unit-origin Line of Sight. Range uses the ordinary ranged Manhattan-distance rule, with the established `RNG 1` Melee special case if such a value ever applies. Range through one Cell pair cannot be combined with LOS through another. Lying enemies are not legal Telekinesis targets.

Resolving Telekinesis lays the chosen target down. It is not an Attack, rolls no Attack or Defence Dice, deals no Damage, and does not create rules opportunities that require an Attack to have occurred.

Telekinesis is the first targeted Action that changes another Unit's Posture. It uses the base Posture rules directly and does not introduce forced movement, saving throws, resistance, immunity, a knockdown/stun condition, or a general posture-effect framework.

### Bonus Actions

Each individual ability explicitly marked `Bonus Action` may be used at most once during a Unit's activation. Different Bonus Action abilities do not compete for a shared Bonus Action opportunity and may be combined during the same activation. As a common baseline, a Bonus Action ability is legal while the Unit's activation is active, that specific ability has not already been used during the activation, and any required uses remain. Individual abilities may define additional legality conditions, such as requiring a valid target or a damaged Unit. Such restrictions are actual ability rules and are distinct from relevance; they should not be inferred merely because using an ability would normally be unhelpful.

Using a Bonus Action marks that specific Bonus Action ability as used for the current activation but does not prevent other Bonus Action abilities from being used. It does not by itself consume or complete Move or Action. After it resolves, legal choices are generated again from the new state.

There is no mandatory Bonus Action phase and no general `Skip Bonus Action` decision. Choosing another choice naturally allows the activation to progress without using a Bonus Action.

Ability names are content identity and may carry theme or lore without defining unique engine semantics. Different named abilities may therefore use the same underlying rules components.

The Barbarian has the passive ability **Fury**:

```text
Fury
Passive
+1 ATK while adjacent to at least 2 enemies.
```

Fury is derived from current Game State. While the Barbarian is Upright and at least two hostile Units are adjacent, Fury contributes one `+1 ATK` bonus. Adjacent uses the established eight surrounding cells plus normal Line of Sight. Additional qualifying enemies do not increase the bonus. Lying hostile Units still count because Posture does not stop a Unit from satisfying another Unit's conditions. A Lying Barbarian does not produce Fury because its own passive abilities are inactive. Fury stacks normally with other applicable modifiers such as Rage.

The first Bonus Action example is the Barbarian ability **Rage**:

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
This Unit has ATK +1 for an Attack against
an enemy adjacent to another friendly Unit.
```

Backstab is derived from the current Game State and the specific target of an Attack. It applies when the target is adjacent to at least one friendly Unit other than the attacking Rogue. Friendly is determined by Side relationship rather than Hero classification, controller, Unit Type, or Behavior. Multiple qualifying friendly Units still provide only a single `+1 ATK` bonus.

For Backstab, adjacent uses the established adjacency rule: one of the eight surrounding cells with normal Line of Sight between the cells. The Rogue itself does not satisfy the requirement merely by being adjacent to its target.

Backstab is target-specific rather than a general change to the Rogue's effective `ATK`. General effective stats describe the Unit before considering a particular Attack target; attack resolution may then apply additional target-specific modifiers to determine the authoritative Attack Dice for that combat. Backstab contributes `+1 ATK` at this target-specific stage.

For example, the Rogue normally has `ATK 3`, and attacks a qualifying Backstab target with 4 Attack Dice. After Throwing Knife, the Rogue's general effective stats are `RNG 3` and `ATK 2`; against a qualifying Backstab target that Attack therefore rolls 3 Attack Dice.

Any rule that evaluates the effectiveness of a specific Attack, including relevance comparison, uses the same authoritative target-specific attack values as actual attack resolution. Backstab must not be reimplemented as special relevance logic or inferred by presentation code.

### Footprint adjacency

**Adjacent** is a defined gameplay relationship, not merely geometric proximity. Two distinct Cells are adjacent when they are within one Cell in any of the eight directions (Chebyshev distance exactly 1) and geometric board LOS exists between those same Cells.

Two distinct Units are adjacent when at least one occupied Cell of one Unit is adjacent to at least one occupied Cell of the other Unit. The **same Cell pair** must satisfy both the eight-neighbor geometry and its required geometric board LOS. This adjacency check does not add hostile-figure blocking; it uses geometric board LOS.

A Unit never counts as adjacent to itself merely because Cells inside its own footprint are adjacent. A multi-Cell Unit still counts as one Unit regardless of how many Cell pairs establish adjacency. Effects such as Fury, Backstab, Aura, Heal, Holy Wave, Cleave, and future defeat consequences therefore count or affect a qualifying Unit once, not once per contacting Cell.

Edges and Doors are not adjacent Units or Cells. An Edge **borders** each of its two Cells. Door rules therefore refer to a Door bordering an occupied Cell rather than defining a separate Unit-to-Edge adjacency relation.

### Free Actions

A Free Action consumes neither the Unit's Move opportunity nor Action opportunity and does not count as using any Bonus Action ability. It may have its own legality conditions, usage limits, exhaustion, or other restrictions.

Free Actions may be available at more than one point during an activation. After a Free Action resolves, legal choices are generated again from the resulting state.

Whether a particular Free Action is legal is part of the rules. Whether a legal Free Action is hidden by normal relevance filtering is a separate presentation/provider concern.

There is no general Free Action phase and no general `Skip Free Action` decision. A legal Free Action appears alongside the other choices currently available to the Unit. If several concrete uses of a Free Action are legal, each is a separate candidate.

The reusable **Open Door** Free Action is available to Units whose Unit Type explicitly has that Free Action or whose Equipment grants it. It is legal whenever the Unit occupies either cell bordering a Closed Door edge, and there is one legal Open Door candidate for each such Closed Door. Resolving the Free Action changes that edge to an Open Door and emits the normal door-open gameplay event.

Open Door has no additional activation-timing restriction. It may therefore be used before or after the Unit's Move and before or after its Action, whenever an adjacent Closed Door makes the Free Action legal and the Unit's activation has not ended. Opening a door consumes no Move or Action opportunity and does not count as using any Bonus Action ability. Legal choices are regenerated afterward, so opening one door may make another Open Door candidate available or change other legal choices.

Open Door is standard Hero content, but is represented explicitly on each Hero Unit Type rather than being inherited implicitly merely because the Unit Type is a Hero. The current Barbarian and Rogue Unit Types both have Open Door.

### Move

Before the Unit has completed its Move, it may choose one legal Move destination. Choosing to remain in the current cell is **Stay** and counts as completing the Unit's Move.

During a Move, a Unit moves from zero through `MOV` steps.

Each step moves to an orthogonally adjacent cell. Diagonal movement is not allowed.

The complete movement is one atomic rules operation. The path may contain multiple cells and is relevant when determining whether the movement is legal, but gameplay effects do not occur between individual steps of the movement.

Rules may therefore trigger before or after the complete movement, but not in the middle of it unless a future rule explicitly overrides this principle.

Choosing Stay still constitutes a completed Move and movement operation. Consequently, a future rule triggered after movement may still trigger when the Unit moved zero steps.

Each movement step translates the Unit's complete footprint one Cell orthogonally. For a 1 × 1 Unit this crosses the ordinary shared Edge between its current Cell and destination Cell. For a 2 × 2 Unit the anchor likewise moves one Cell and all four occupied Cells translate together; there is no turning or rotation.

A movement step is legal only when the complete footprint remains on the board, every terrain Cell traversed by the footprint is passable under that Unit's traversal rules, and every Edge crossed by translating the footprint is passable under those traversal rules. A 2 × 2 Unit therefore cannot pass through a one-Cell-wide opening or corridor. If a route around such an opening exists, pathfinding may find it; if no fitting route exists, the destination is unreachable.

The complete footprint must also be a legal stopping placement at the final destination, including the normal internal-Edge placement rule. Traversal permissions do not by themselves make an otherwise illegal footprint a legal stopping placement.

For the currently defined edge features:

- a wall is impassable,
- a closed door is impassable,
- an open door is passable.

Other edge features may define their own movement passability when introduced.

A Unit may pass through Cells occupied by friendly Units during its movement; for a multi-Cell footprint, overlap with a friendly footprint may occur during traversal.

A Unit may not pass through any Cell occupied by a hostile Unit. Any footprint intersection with a hostile Unit therefore blocks that traversal position.

A Unit may never end its movement with any part of its footprint overlapping another Unit, whether friendly or hostile.

Whether a destination cell is otherwise passable is determined by the rules for its terrain or fixed object. Those rules are deliberately deferred to the corresponding terrain specification.

### Resources and Transport Resource

A **Resource** is a loose physical marker on a Board Cell, not a Unit, Figure, Terrain Tile, or fixed object. Each Resource has a Resource Type. The initial authored Resource Type is **Coal**; additional types use the same rules.

- A Cell may contain at most one Resource, regardless of Resource Type.
- A Resource on the Board has no Side or controller.
- A Resource does not occupy a Unit's space, prevent placement or movement through or onto its Cell, change terrain or Edge passability, or affect Line of Sight. Units may stand on Resources.
- Resource Type and Cell are authoritative state, representable by a distinguishable physical marker on the Board. A Resource remains where it is until a rule explicitly moves or removes it.

**Transport Resource** is an optional Unit Type Capability. During a Unit's *own Move* (including a Move provided by `MoveAfterAttack`), if the Unit has Transport Resource and its starting Cell contains a Resource, that Resource accompanies the Unit to its final destination Cell **only if that destination does not already contain a Resource**.

- The Unit's complete Move and any Resource relocation are a single atomic rules operation. There is no intermediate Cell at which the Resource can be picked up, dropped, or affected.
- If the destination already contains a Resource of any type, the Unit still performs its otherwise legal Move, but the Resource on its starting Cell remains there. Neither Resource is replaced or displaced.
- Resources on intermediate Cells of the movement path are not picked up, transported, or otherwise affected.
- Stay (a Move of zero steps) does not change any Resource.
- Transport Resource does not itself change legal movement paths or destinations. It does not require or cause the Unit's Behavior to seek Resources.
- A Unit without Transport Resource does not relocate Resources by moving.
- External repositioning of a Unit, including effects that move another Unit, push, pull, or Swap Places, does not transport Resources. Such effects are not the affected Unit performing its own Move, even when the Unit has Transport Resource.

This initial transport rule concerns a one-Cell Unit and the Resource on that Unit's starting Cell. Transport by a larger-footprint Unit is not specified and must not be inferred from this rule.

The canonical Zombie Unit Type has **Transport Resource**; its Behavior and other abilities remain unchanged.

### Resource Storage, Pickup Resource, and Give Resource

**Resource Storage** is an optional Unit Type Capability specifying accepted Resource Types and a maximum stored quantity for each. Stored Resources are represented by physical markers on the Unit Type's card. Storage belongs to the combination of **Side + Unit Type**, not to an individual figure: all Units of that Type on that Side share the same stored quantities and capacities. Different Sides and Unit Types have independent stores. Stored quantities are authoritative Game State and must remain representable in the physical game.

Storage and Resource-handling Actions are independent. Resource Storage does not automatically grant any Action, and an Action cannot deposit a Resource into a recipient that has no capacity for its Type. A Resource cannot be deposited beyond the receiving store's capacity. Transferring one Resource from a Cell into storage removes its Board marker and increases the stored quantity by one, as a single atomic rules operation. Failed or unavailable transfers do not change either location.

**Pickup Resource** is a Unit Type Action. The acting Unit may take one Resource from the Cell it currently occupies and place it in its own shared Side + Unit Type storage, provided that storage accepts the Resource Type and has room.

**Give Resource** is a Unit Type Action. The acting Unit may take one Resource from the Cell it currently occupies and give it to an adjacent friendly Unit whose shared Side + Unit Type storage accepts that Resource Type and has room. The recipient does not need Pickup Resource. The acting Unit does not need Resource Storage. If multiple eligible recipients exist, the acting controller chooses a recipient through the normal Action decision rules.

Pickup Resource and Give Resource are ordinary Actions and use the Unit's normal single Action opportunity. They do not constitute movement and do not invoke Transport Resource.

The canonical Shaman retains its original Summon Goblin Action and Flee/Summon Behavior; it has neither Resource Storage nor Pickup Resource.

**Geomancer** is a separate non-attacking Monster Unit Type with stats `MOV 2, RNG 0, ATK 0, DEF 3, HP 1`, `ResourceStorage(Diamond, 1)`, and three Actions: **Pickup Resource**, **Summon Worker**, and **Summon Troll**. **Diamond** is a Resource Type. Geomancer has no normal Attack.

- **Summon Worker — Action:** If fewer than three living Workers of the canonical Worker Unit Type are currently in play on Geomancer's own Side, place one **Upright Worker** in a legal adjacent empty Cell. This cap is per Side and counts Workers regardless of their controller. A Worker summoned this way has Geomancer's Side and the canonical Worker's normal stats and abilities.
- **Summon Troll — Action:** Spend one Diamond from Geomancer's shared Side + Unit Type Resource Storage and place one **Upright Troll** in a legal adjacent empty Cell on Geomancer's Side. The Action is available only when at least one Diamond is stored and at least one legal placement exists. The spending and placement are atomic; no Diamond is consumed if the Action cannot resolve.

Both Actions use the existing Shaman Summon Goblin placement rules for adjacency, LOS, terrain, Edge and Unit occupancy, except for the summoned Unit Type, the extra prerequisites and **Upright** starting Posture. Summoned Units follow the existing activation-bag snapshot semantics and do not add tokens to the current bag.

**Geomancer Behavior** is a reusable composition of Resource-seeking movement, Flee, and Action preferences, not special legal-move or summon rules:

- **Move:** If Geomancer's shared Diamond storage is empty and a Cell containing Diamond is a legal reachable Move destination **this activation**, including its current Cell through **Stay**, prefer the nearest such Cell (shortest actual movement path; then top-to-bottom, left-to-right). If Geomancer already stands on a Diamond, prefer Stay (distance 0), allowing Pickup Resource in its Action phase. Otherwise use normal **Flee**. This does not authorize pursuit of a Diamond beyond the current Move range.
- **Action:** Prefer, in order, (1) Summon Troll if legal; (2) Pickup Resource if legal **for Diamond**; (3) Summon Worker if legal; (4) Skip. For automated target placement choose the top-left legal Cell, as with Shaman. Ordinary legality governs every option.
- As with other Behaviors, these are automated preferences only; they do not grant Actions or restrict choices for human-controlled Geomancers.

In particular, a Geomancer may summon a Troll using a Diamond it already held even if it moved away from a Diamond on the Board.

### Worker and Worker Behavior

Worker is a canonical Unit Type with MOV 4, RNG 0, ATK 0, DEF 2, HP 1; CantAttack, Transport Resource, Give Resource, and Operate. It has no Resource Storage.

Worker Behavior is reusable by other Unit Types and has no hardcoded Worker type check. It selects among existing legal decisions and does not alter legality. When evaluating potential Machine recipients and operating opportunities, it must respect Machine ownership (own Side or public); inaccessible machines are not viable work targets. Each Unit completes its activation before the next grouped Unit. No task reservation or persistent work plan exists.

Its priorities are:
1. If standing on a Resource with a viable recipient with available capacity, move toward the nearest reachable position from which it can Give that Resource; Give when legal. Do not choose a resource-occupied destination that would leave this deliverable Resource behind.
2. Otherwise, Operate an adjacent Machine if that Action is legal.
3. Otherwise, approach the closest reachable Resource with a potential recipient that has capacity. If the starting Resource is not useful, intentionally moving onto another useful Resource is allowed: the old Resource stays on its starting Cell.
4. Otherwise, approach the nearest ready Machine that offers a reachable operating position.
5. Otherwise, Stay and Skip.

Work preferences are reconsidered at each decision opportunity using current Game State. Use deterministic tie-breaking: shortest relevant path, then top-left position, then stable target identity where required. Do not choose inaccessible targets. Legality of Operate already excludes a Resource on the operator's Cell.

Worker Behavior honors Do Nothing Standing Order. All other Orders allow its normal work preferences; this is a Behavior-specific interpretation, not a general rules change. Worker Behavior does not attack, plan downstream production, or grant Actions.

### Follow Route Standing Order

**Follow Route** is a reusable Standing Order for a **Unit Type + Side group**, not a separate racing controller. It references a scenario-defined **Route** by stable Route ID. A Route contains an ordered nonempty list of stable **Waypoint IDs** and a traversal mode: **Once**, **Loop**, or **Ping-pong**. Each **Waypoint** is a distinct scenario object with a stable unique ID and an on-board rectangular area consisting of one or more Cells. Multiple Routes may reference the same Waypoint, and multiple Unit Type + Side groups may reference the same Route. Waypoints and Routes are static scenario definitions, not Units, Terrain, Cell Markers, or controllers; they have no independent activation, Side or mutable gameplay state. Route IDs and Waypoint IDs are unique within their respective scenario catalogs. Every Route reference and every Waypoint reference must resolve to a valid definition; order and repeated references are preserved. Scenario authoring, serialization, validation, state projections and Level Designer must support creating/editing/removing these definitions and assigning a Route to a group, with referential integrity on edits/deletion. The group shares one authoritative current waypoint index and, for Ping-pong, traversal direction. These values are serialized as part of the group's Standing Order state, not separately on each Unit. The Follow Route Order stores the Route ID plus group-specific progress (current index and Ping-pong direction); Route and Waypoint definitions remain shared and are not copied into each Order. Initial progress may be authored, and assigning/replacing a Route Order initializes progress according to the established order-setting semantics. Different groups following the same Route advance independently.

Waypoint areas are presentation geometry, not obstacles or changes to Terrain, LOS or movement legality. In Enhanced 2D, draw a subtle, non-interactive outline around each Waypoint rectangle, optionally with a small identifying label. Keep it distinguishable from selected cells, move highlights and Cell Markers, and below Units. Show/edit Waypoints and Routes in the Level Designer, including rectangle geometry, waypoint identities, route ordering/mode and group assignment. Classic View retains usable fallback presentation. The route overlay may be toggled for readability, but its visibility never affects gameplay.

Reaching a waypoint may emit an authoritative **Waypoint Reached** occurrence containing Waypoint ID, Route ID, Unit Type + Side and the arriving Unit ID, for presentation and for abilities such as Waypoint Slot Machine. The occurrence does not itself award resources, trigger racing-only logic or advance unrelated groups. Future waypoint-specific rewards are deferred.

A waypoint is reached only when a group Unit **ends its ordinary Move** on a Cell in the group's current waypoint area (for multi-Cell Units, any occupied destination Cell within the area). Merely passing through the area along an atomic Move path, being placed there by an external effect, or already occupying it without completing a Move does not advance the route. A deliberate Stay at the waypoint counts as ending the Move there. Each completed Move can advance **at most one** waypoint. A group Unit that reaches the current waypoint immediately advances the shared order for all subsequent decisions, including later Units in the same sequential group activation; already completed activations are not revisited.

**Once** completes and clears the Follow Route Order when its final waypoint is reached, returning to Normal Behavior thereafter. **Loop** advances from the last waypoint to the first. **Ping-pong** reverses at either end without repeating the endpoint: for at least two waypoints, the sequence is 0, 1, ..., n-1, n-2, ..., 1, 0, 1, ... . A single-waypoint Loop or Ping-pong route continues to target its sole area. Completion/advancement is deterministic and does not grant additional Moves or Actions.

For ordinary Move decisions under Follow Route, evaluate existing legal destinations and their hypothetical route distances to the current waypoint area, respecting the moving Unit's traversal capabilities and the established planning/reachability distinction (including ignoring Banana Peels as hypothetical route obstacles). If at least one legal destination lies in the current waypoint area, prefer such destinations; otherwise prefer legal destinations that make progress toward that area. Use canonical deterministic tie-breaking. If no legal Move makes useful progress, Stay when available. Actions and other decisions continue using the Unit's Normal Behavior, unless an existing higher-priority Standing Order rule says otherwise.

**Route Lookahead** is a reusable optional movement preference layered onto Follow Route, not a separate route state or Unit Type-specific pathfinder. When multiple legal Move destinations reach the current waypoint, choose the one with the shortest hypothetical traversable distance to the **next** waypoint area in the route's actual Once/Loop/Ping-pong sequence. If there is no next waypoint (terminal Once), use ordinary deterministic tie-breaking. Lookahead does not advance progress early, examine intermediate cells for waypoint completion, grant extra movement, or change legality. Route orders remain applicable to grouped Units such as Zombies and single-Unit groups such as a Unique Diamond Kart.

### Racing Resources, Diamond Kart, and racing abilities

**Banana**, **Mushroom**, and **Shell** are distinct Resource Types, separate from Diamond. **Banana** is a stored/loose Resource; **Banana Peel** is a Cell Marker created by an ability, not a Resource. The shared Unit Type + Side Resource Storage and existing transfer rules apply.

**Diamond Kart** remains a canonical Unique Unit Type with `MOV 5, RNG 0, ATK 0, DEF 3, HP 4`, `CantAttack`, and Asphalt-restricted ordinary movement. Replace its former Diamond storage and Diamond-cost Place Banana Peel Action with `ResourceStorage(Banana, 1)`, `ResourceStorage(Mushroom, 1)`, `ResourceStorage(Shell, 1)`, **Place Banana Peel (Banana cost 1)** as a Bonus Action, **Boost (Mushroom cost 1)** as a Bonus Action, and **Shoot Shell (Shell cost 1, RNG 4, ATK 3)** as a normal Action. Its assigned Follow Route/Route Lookahead remains responsible for movement; no Kart-specific route state is introduced.

**Place Banana Peel** consumes one stored Banana and places a Banana Peel marker on a legal adjacent Cell using the existing placement restrictions (passable Cell, no Unit/Machine/Cell Marker; Resources allowed). Asphalt is not a legality requirement. The spending and placement are atomic. It is now a **Bonus Action** and therefore does not consume the normal Action opportunity. Automated Kart behavior uses it after all available Moves (including Boost-granted Moves) have been completed, whenever a legal placement exists. Prefer Asphalt targets and the previously specified route-distance/tie-breaking priorities; no tactical avoidance of friendly slips is required.

**Boost** consumes one stored Mushroom and grants **one additional Move opportunity during the current activation**. It is a Bonus Action, legal while the Unit is active and has not yet used any normal Action; it does not require the additional Move to be useful or have a non-Stay destination. It may be used even after an earlier Move has completed, reopening Move availability. Automated Kart behavior uses Boost as soon as legal, regardless of benefit. The extra Move obeys ordinary movement, terrain restriction, Banana Peel arrival and Follow Route progress rules.

**Shoot Shell** consumes one stored Shell and performs a separate targeted attack effect with `RNG 4` and `ATK 3`, using the ordinary attack-range, LOS, hit/defense and damage resolution for a legal hostile Unit target. It is a normal Action and is legal only with a valid target and stored Shell. `CantAttack` prohibits the normal Attack Action but does not prohibit this explicit ability. The cost and attack resolution form one ability use. Automated Kart behavior selects the nearest legal hostile target, using deterministic existing distance/tie-breaking conventions; it does not reserve Shell for a more valuable target. Without a valid target, it keeps its Shell.

**Move opportunities per activation:** A living Unit normally has one Move opportunity; rules may add or subtract opportunities, with an effective minimum of zero, analogously to existing Action opportunity modifiers. Track completed Moves against the currently effective number of available Moves. **All available Move opportunities must be completed (Move or Stay) before any normal Action becomes legal.** Bonus Actions and Free Actions retain their existing flexible timing and individual legality/use limits. An ability that adds a Move after an earlier Move has been completed makes that additional Move available; such a Move-granting ability is not legal after a normal Action has been used. End Turn requires all available Moves to be completed, as well as the existing mandatory-resolution conditions. Each completed ordinary Move can advance at most one Follow Route waypoint, including when it is an additional Move.

**Kart automated choice priority:** Use Boost immediately when legal; choose available Moves according to Follow Route and Route Lookahead; once all Moves are completed, use Place Banana Peel when legal; use Shoot Shell at the normal Action opportunity when a legal target exists. Existing generic action/bonus legality and Do Nothing behavior remain authoritative; human controllers may choose other legal timings/targets.

### Racing Slot Machine and Fruit Tree

**Fruit** is a Resource Type, represented thematically by cherries; the name does not specify a botanical species. **Fruit Tree** is Terrain with the same base properties as Tree: `Passable = no`, `Blocks LOS = yes`. At Round Start, each Fruit Tree generates Fruit using the **same two d8 attempts, directional numbering, Cell eligibility, LOS, resolution ordering and timing as Lava generating Coal**, substituting Fruit for Coal. **For this generation LOS check only, ignore the producing Fruit Tree Cell's own LOS-blocking property; retain all other terrain, Edge, corner and geometric LOS restrictions.** This exception does not change the Fruit Tree's normal LOS blocking for any other purpose. Fruit Tree and Lava are independent sources and do not alter each other's rules.

The reusable **Racing Slot Table (d6)** yields: **1–3 Banana; 4–5 Mushroom; 6 Shell**. Roll once per award/operation; if the selected output cannot be stored or placed, do not reroll or substitute another result.

**Waypoint Slot Machine** is a Kart automatic end-of-activation reward: if that Kart's own completed Move advanced its group's Follow Route waypoint at least once during this activation, roll once on the Racing Slot Table at the end of its activation and attempt to add one resulting Resource to its shared Unit Type + Side storage. If that Resource's capacity is already full, nothing is added. The award cannot be spent earlier in that same activation. Merely benefiting from another group member's route advance does not qualify the Kart. Multiple waypoint advances in one activation still grant only one roll.

**Slot Machine** is a fixed Machine with `ResourceStorage(Fruit, 3)`. Its Operate action consumes three stored Fruit and produces one Resource selected by the same Racing Slot Table, on the operating Worker's Cell under the existing Machine output placement rules. It uses the existing Machine ownership, Give Resource and Operate mechanisms. A failed output placement does not consume input under the normal atomic operation requirements.

### Chief Mechanic and Prefer Summon Worker Behavior

**Chief Mechanic** is a canonical **Unique** Unit Type with stats `MOV 3, RNG 0, ATK 0, DEF 4, HP 3`. It has `CantAttack`, `TransportResource`, `GiveResource`, `Operate`, and `SummonWorker(sideLimit: 3)`. It has no Resource Storage. Its Unique constraint is per Side, so opposing Sides may each field one Chief Mechanic.

Chief Mechanic combines the reusable **Worker** Behavior with a reusable **Prefer Summon Worker** Action preference. Its ordinary Move decisions use Worker Behavior unchanged. When selecting an Action, prefer **Summon Worker** whenever it is legal, before Worker Behavior's normal Give Resource / Operate / other Action preferences; otherwise use Worker Behavior unchanged. This preference does not grant an Action, change legality, or override Do Nothing Standing Order. For multiple legal Summon Worker placements, use the existing deterministic Summon Worker target selection convention. The summoned Unit is the canonical Worker, and the Side limit counts living canonical Workers on the Chief Mechanic's own Side, consistently with Geomancer.

### Machines as terrain objects

A **Machine** is an individually identifiable fixed terrain object occupying one Board Cell. Its Cell blocks Unit movement/placement and Line of Sight, like other obstructing terrain objects. A Machine does not act, activate, or have a Unit Type, Activation Token, or Unit HP/DEF under these initial rules. It may optionally have a permanent Owner Side, as specified below. Machine destruction is deferred.

Unlike shared Unit Type storage, **each Machine instance has its own Resource Storage**, with accepted Resource Type(s), capacity per type, and current quantities. Place stored Resource markers on or beside the individual Machine's physical component so its state is identifiable. The Game State and scenario definitions preserve each Machine's individual location, type/operation, and storage.

**Machine ownership** is optional per individual Machine instance: either no Owner Side (public, usable by all Sides) or a permanent Owner Side identifying a Side in the scenario. Ownership is set by scenario content, is authoritative and physically identifiable, and cannot change during play under these rules. A Machine's ownership restricts Give Resource and Operate to acting Units belonging to its Owner Side; a public Machine accepts those Actions from any Side. No hostility or friendly-alliance exception grants other Sides access to an owned Machine. Resources on Cells, including a Machine's output on an operator's Cell, remain unowned. Ownership does not change Machine storage, terrain, LOS, or resource transport rules.

**Give Resource** may target an adjacent Machine in place of a friendly Unit if the Machine accepts the Resource on the acting Unit's Cell, has available capacity, and its ownership allows the acting Unit's Side. Giving removes the Cell Resource and adds one to the specific Machine's store atomically, consuming the acting Unit's normal Action opportunity.

A Machine defines one **Operation** with specific Resource costs and an output Resource Type and quantity. For the initial concrete operation, **Diamond Press** has `ResourceStorage(Coal, 3)` and `Spend 3 Coal → Output 1 Diamond`. The Operation consumes the stated inputs from that specific Machine's storage, not from another Machine or a Unit's shared storage.

**Operate** is an optional Unit Type Action. An acting Unit adjacent to a Machine may choose it as the Operate target if:
- a normal wall, closed door, or other impassable Edge does not separate the Unit's Cell from the Machine Cell; ordinary adjacency by itself does not override a blocking Edge;
- the Machine is public or its Owner Side matches the acting Unit's Side;
- that Machine has all Resources required by its Operation;
- the acting Unit's Cell contains no Resource, so the output has room.

When Operate resolves, its Resource costs are removed from that Machine and the output Resource is placed on the operator's Cell as one atomic operation. The initial Operation produces exactly one Resource. Machines with outputs of other quantities or additional output-placement rules are deferred.

Machines must be allowed next to Walls and other Edge features, including an Edge immediately between the Machine Cell and an adjacent Cell. An impassable Edge prevents a Unit on that side from Operating the Machine, even when geometrically adjacent. No new general LOS rule for Machine interaction is implied.

Giving to a Machine and Operating it are distinct ordinary Actions, so a Unit cannot do both within its normal single Action opportunity. Machine construction, changing Machine ownership, combat against Machines, and other recipes are deferred.

### Moving and repositioning Units by effects

An effect may instruct a player to move or reposition a Unit without giving that Unit its ordinary **Move**. This changes the figure's position but is not that Unit performing a Move, does not spend its `MOV`, and does not satisfy or trigger rules that refer to that Unit moving or performing a Move unless the effect explicitly says otherwise.

When an effect moves a 1 × 1 Unit **up to 1 Cell**, choosing 0 Cells leaves it in its current Cell. Choosing 1 Cell uses one ordinary movement step: movement between Cells is orthogonal, so the destination is one of the four orthogonally neighboring Cells. That one-Cell relocation must obey ordinary terrain, Edge, board-boundary, placement, and Unit-occupancy legality for that step. Movement capabilities belonging to the affected Unit, such as Phase, do not alter an external effect's relocation unless the effect explicitly uses them.

**Swap places** exchanges the Cells occupied by two 1 × 1 Units simultaneously. Neither Unit performs a Move. When a rule requires the Units to be adjacent, the ordinary adjacency rule already establishes the required local geometric relationship between their Cells, including geometric board LOS. The two Units do not block the exchange by occupying each other's destination Cells.

The general large-footprint restriction under Footprint applies to all such external movement and repositioning. A Unit larger than 1 × 1 cannot be affected by another Unit's move, reposition, or swap effect unless that effect explicitly overrides the restriction.

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

The engine determines all legal footprint placements reachable by a Unit within its effective movement allowance while applying movement legality. A reachable destination is represented by the figure's anchor Cell and is exposed at most once. Pathfinding nodes therefore represent placements of the complete footprint, not independent movement of its occupied Cells.

For each reachable destination, the engine associates one deterministic shortest legal path from the Unit's starting anchor Cell to that destination anchor. A flood-fill or breadth-first search is an appropriate implementation for the current uniform-cost grid movement.

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

### Rules language and resolution timing

Canonical Ability text is the primary player-facing specification of an Ability, interpreted using the shared rules and defined terms. Code implements that meaning; card text is not parsed.

The following words have consistent rules meaning:

- **When X, Y** creates an automatic triggered consequence if X actually occurs. The consequence is appended to the end of the automatic-consequence queue; it does not interrupt the effect currently resolving.
- **If X would happen, Y instead** is a replacement. Replacement effects are checked and resolved immediately before X would occur because they determine whether X occurs at all.
- **After X** creates a follow-up at the boundary after X and the automatic consequences caused during its resolution have completely resolved. Follow-ups resolve or are declined before ordinary activation choices resume.
- **May** marks optional performance. Without `may`, an effect instruction is mandatory.
- **While** states a continuous condition evaluated from current state; it is not a trigger.
- **Every** and **all** include every qualifying Unit once without choosing recipients.
- **Choose** explicitly requires agency among legal alternatives. The word **target** does not itself imply that an object was chosen.
- **Another** excludes the referenced source Unit.
- **Up to N**, when specifying a selectable quantity, permits choosing any amount from zero through N. It is not used merely to express an automatic cap such as maximum HP.
- Instructions resolve in written order. **Then** explicitly separates sequential instructions.

An **Attack target** is a Unit included in that Attack's fixed target set, whether selected individually or included automatically by the Attack. A Unit affected by a non-Attack effect is not a target merely because the effect applies to it. References such as `the targets` refer back to the fixed targets of the relevant Attack; later changes to position, Range, LOS, adjacency, or Posture do not recalculate that set.

When an effect begins resolving over **every/all** qualifying Unit, determine that recipient set at that point. Resolve the effect for those recipients in the rules-defined deterministic order without player-selected ordering. Later changes to position, adjacency, LOS, or other eligibility do not remove a Unit from that already-established set. A Unit that has left play before its pending application is reached can no longer be affected and is skipped unless a rule explicitly says otherwise.

Automatic triggered consequences use a FIFO queue. Finish the currently resolving effect before resolving the queued consequence at the front. A new `When` consequence created while another effect or consequence is resolving is appended to the end of the queue. Continue until the queue is empty before exposing an `After` follow-up or returning to ordinary choices. This is a physical resolution procedure, not a stack, priority system, or opportunity for players to reorder consequences. If future content makes several consequences arise from the exact same event and their relative order matters, that content must define a deterministic order rather than introducing player-selected ordering implicitly.

Defeat-triggered consequences remain valid after their source has left play. They may use the defeated Unit's relevant state and footprint as it existed at the moment of defeat when the consequence needs that information. This is remembered only while the automatic resolution sequence is in progress and therefore does not create persistent hidden game state.

### Immediate follow-ups

Some rules may create an immediate follow-up after another operation has fully resolved. A follow-up is not a separate Action or Bonus Action category. It is a continuation of the rule that created it and must be resolved or declined, when optional, before ordinary activation choices resume.

Follow-ups use the normal authoritative decision boundary when they require agency. A mandatory follow-up may permit only its follow-up choices; an optional follow-up also permits declining it. Once the follow-up is resolved or declined, ordinary legal choices are generated again from the resulting state.

The Goblin's Move After Attack is the first mandatory example. The Barbarian ability **Cleave** is the first optional example:

```text
Cleave [2/game]

After this Unit's Attack, if it dealt 2 or more damage
to a Unit, you may immediately deal 1 damage to an adjacent enemy.
```

Cleave starts each game with 2 remaining uses and a maximum of 2. After the Barbarian's Attack has fully resolved, if that Attack dealt at least 2 Damage to at least one Unit, Cleave has at least one use remaining, and at least one legal Cleave target exists, an optional Cleave follow-up is created. Damage dealt to different targets is not added together for this condition. Ordinary activation choices do not resume until the Barbarian either uses Cleave or declines that follow-up.

Each adjacent hostile Unit is a separate legal Cleave target. For Cleave, adjacent uses the same eight surrounding cells and normal Line of Sight requirement used by other adjacency rules. Choosing a target immediately spends one Cleave use and deals 1 Damage directly to that target. This Damage is not an Attack and does not roll Attack or Defence Dice. Current HP is reduced by 1, never below zero, and normal death resolution applies.

Declining Cleave spends no use. Whether used or declined, that particular follow-up then ends and ordinary legal choices are generated again. Cleave has no once-per-activation restriction of its own: if a future rule allows the Barbarian to make another Attack during the same activation and that Attack independently deals at least 2 Damage to a Unit, it may create another Cleave follow-up if a use remains.

Cleave does not require persistent activation history such as the last choice made or the maximum Damage dealt earlier in the activation. Its eligibility comes directly from the Attack that has just resolved, preserving the physical rule that Cleave is an immediate continuation rather than an opportunity that can be saved for later.

## World Effects

A scenario may use a physical **World Deck** to add global effects at Round Start. A scenario using World Effects specifies how many World Cards are drawn each Round and a **Cycling N** value, where N is at least 1. `Cycling 1` is the normal baseline; larger values deliberately permit more simultaneous and potentially chaotic combinations.

World Cards are individual physical card instances, not weighted random effect definitions. Each card is in exactly one relevant physical location: the draw pile, the discard pile, or, for an active Continuous card, the active World Effect row.

At Round Start, draw the scenario's specified number of World Cards **one at a time**. Completely resolve each draw, including all automatic consequences it creates, before drawing the next card.

Round Start World Cards are resolved **before the Activation Bag is populated for that Round**. Completely resolve all World Card draws and all automatic consequences they create first. Only after the final World Card resolution has reached a stable state is the Activation Bag populated from the Unit Type + Side combinations then in play. Units defeated or otherwise removed during Round Start therefore do not contribute Activation Tokens, while Units created during Round Start do contribute according to the ordinary Activation Bag rules.

If the draw pile is empty when a card is to be drawn, shuffle the discard pile to form a new draw pile, then continue the draw. Active Continuous cards are not part of the discard pile and therefore are not included in that reshuffle. If both draw pile and discard pile are empty, no card is drawn.

World Cards are one of two kinds:

- **Immediate** — resolve the card immediately, including the ordinary automatic-consequence queue, then put it in the discard pile. Continuous World Effects already active remain in effect throughout this resolution.
- **Continuous** — make the new card active immediately. The active Continuous cards are ordered oldest to newest. If adding the new card would exceed the scenario's Cycling N, the oldest active card simultaneously ceases to be active and moves to the discard pile. This is one atomic state change with no observable intermediate state in which both too many cards, or too few cards, are active.

A Continuous effect applies for exactly as long as its card is active. Multiple active modifiers stack additively. Opposing modifiers therefore cancel numerically while both remain active, but the cards remain distinct physical cards and retain their positions in the Cycling order.

A **No Effect** card is a Continuous World Card. It has no modifier or other effect, but occupies an active Cycling position normally and can therefore cycle out an older Continuous card.

World Effects use the ordinary rules-resolution model even though they resolve outside a Unit activation. An effect over all qualifying Units establishes its recipient set when the effect begins and resolves recipients in deterministic top-to-bottom, then left-to-right Board order. For multi-Cell Units, use the Unit's anchor Cell for this ordering. Consequences such as `When this Unit is defeated` are appended to and drained from the ordinary FIFO automatic-consequence queue. The complete World Card resolution and its automatic consequences finish before the next World Card is drawn.

### World modifiers

World Effects may modify effective `ATK`, `DEF`, `MOV`, Actions per activation, or uses of each unique Bonus Action per activation. These effective numeric values cannot be reduced below 0.

Stat modifiers do not grant capabilities that a Unit does not otherwise have. In particular, effective `ATK 0` prevents a Unit from making a normal Attack, while effective `ATK > 0` does not itself grant a normal Attack or override a rule such as `CantAttack`.

A Unit normally has one Move opportunity per ordinary activation; Move opportunity modifiers change that number without reducing it below zero. All effective Move opportunities must be completed before normal Actions are legal, as specified under Racing Resources and Diamond Kart. A Unit normally has one Action opportunity per ordinary activation. An Action modifier changes that number. If a Unit has more than one Action opportunity, it may use the same Action more than once unless that Action has another restriction. Each use resolves completely before another Action is performed.

Each unique Bonus Action may normally be used at most once per activation. A Bonus Action use modifier changes that per-unique-Ability limit for every Bonus Action. This does not replenish or bypass a limited-use Ability's remaining uses: every performance still spends a use normally when applicable.

### Initial World Deck

The first World Deck is intended as a roughly 40-card baseline deck balanced primarily around `Cycling 1`. Card names are content and may be revised without changing their mechanics.

Continuous card types:

- **Bloodlust** — All Units have ATK +1.
- **Weakness** — All Units have ATK -1.
- **Iron Skin** — All Units have DEF +1.
- **Vulnerability** — All Units have DEF -1.
- **Haste** — All Units have MOV +1.
- **Sluggishness** — All Units have MOV -1.
- **Frenzy** — All Units have +1 Action per activation.
- **Fatigue** — All Units have -1 Action per activation.
- **Surge** — Each unique Bonus Action may be used one additional time per activation.
- **Hesitation** — Each unique Bonus Action may be used one fewer time per activation.
- **Calm** — No Effect.

Immediate card types:

- **Earthquake** — Lay down all Units.
- **Open Sesame!** — Open all Doors.
- **Lockdown** — Close all Doors.
- **Miracle** — Fully heal all Units.
- **Second Wind** — Heal every damaged Unit for 1 HP.
- **Repulsion** — Deal 1 damage to every Unit adjacent to another Unit.
- **Loneliness** — Deal 1 damage to every Unit not adjacent to another Unit.
- **Renewal** — Replenish 1 use of every limited-use Ability.

`Fully heal` restores current HP to maximum HP and does not change Posture. Ordinary healing cannot increase current HP above maximum HP and does not change Posture.

`Replenish 1 use` increases the remaining uses of every limited-use Ability on every Unit by 1, up to that Ability's normal maximum. Unlimited Abilities are unaffected.

For Repulsion and Loneliness, `another Unit` uses ordinary Unit adjacency and ignores Side. A Unit is affected at most once regardless of how many other Units it is adjacent to.

Open Sesame! changes every Closed Door to Open. Lockdown changes every Open Door to Closed. They operate directly on Door state and do not perform a Unit's door Action.

The initial physical composition target is 40 cards:

| Card | Copies |
| --- | ---: |
| Bloodlust | 3 |
| Weakness | 3 |
| Iron Skin | 3 |
| Vulnerability | 3 |
| Haste | 3 |
| Sluggishness | 3 |
| Frenzy | 1 |
| Fatigue | 1 |
| Surge | 1 |
| Hesitation | 1 |
| Earthquake | 1 |
| Open Sesame! | 1 |
| Lockdown | 1 |
| Miracle | 1 |
| Second Wind | 2 |
| Repulsion | 1 |
| Loneliness | 1 |
| Renewal | 2 |
| Calm | 8 |

Different future World Decks may use different card compositions and effects. They should be introduced from concrete content needs rather than by creating a speculative generic effect language.

## Normal attacks

A normal Attack is a basic Action.

A Unit can make a normal Attack only when both its effective `RNG` and effective `ATK` permit it. Base stats may be modified by rules or abilities. General effective stats are derived without assuming a particular target. When an Attack targets a specific Unit, target-specific rules may further modify the values used for that combat. Attack resolution uses these authoritative target-specific attack inputs where applicable rather than treating the Unit's general effective stats as necessarily final.

The first target-specific attack modifier is the Rogue's Backstab passive. Broader categories, ordering, or a universal combat-modifier framework are deliberately deferred until concrete content requires them.

### Range

For ranged attacks, `RNG` is the maximum orthogonal distance between an occupied Cell of the attacker and an occupied Cell of the target. Orthogonal distance is Manhattan distance: the absolute horizontal difference plus the absolute vertical difference between the two Cells.

`RNG 1` is a special case called **Melee**. A Melee attack may target a Unit when an occupied Cell of the attacker and an occupied Cell of the target are among the ordinary eight neighboring Cells, including diagonal adjacency. A physical Unit card may display `Melee` rather than the numeric value `1` to make this distinction explicit.

A target must satisfy both the applicable range rule and Line of Sight through the **same occupied-Cell pair**. It is not sufficient for one pair of Cells to establish Range while a different pair establishes LOS. For multi-Cell Units, a legal normal Attack therefore exists when at least one source/target occupied-Cell pair simultaneously satisfies the applicable Range rule and ordinary Unit-origin LOS.

When a rule needs Unit-to-Unit distance without attack legality, use the minimum applicable Cell-to-Cell distance over the Units' occupied Cells unless that rule defines a different distance measure.

### Line of Sight

Line of Sight (LOS) preserves the Cell-center-to-Cell-center geometric primitive. A Unit has Unit-to-Unit LOS when at least one of its occupied Cell centers has ordinary LOS to at least one occupied Cell center of the other Unit. A 2 × 2 figure has no separate geometric center for LOS.

Range and LOS remain distinct properties, but when a rule such as Normal Attack requires both, the same occupied-Cell pair must satisfy both as specified by that rule.

LOS blocking is based on logical grid geometry rather than the detailed physical silhouette of a miniature or terrain model.

A cell is either completely LOS-blocking or completely non-blocking. If a type of terrain or fixed object is defined as LOS-blocking, the entire area of its cell blocks LOS regardless of the object's actual physical shape within the cell.

Likewise, an edge feature either blocks LOS at the point where the LOS line crosses that edge or does not. For example, an open door does not partially obstruct an edge merely because a physical door model remains present.

An open door does not block LOS. Opening a door therefore immediately allows otherwise legal attacks through that edge, including hypothetical attacks used to rank Monster movement destinations.

Because LOS runs between cell centers, it cannot run along a grid edge. It can, however, pass exactly through a grid corner.

When LOS passes exactly through a grid corner, consider the two flank Cells beside the straight line, excluding the Cell the ray arrives from and the Cell it enters. Each flank defines one possible corner passage. A passage is closed when its flank Cell contains LOS-blocking terrain or a LOS-blocking fixed object, or when an LOS-blocking Edge separates that flank Cell from either the arriving Cell or the entered Cell. LOS is blocked at that corner only when **both passages are closed**. If at least one passage is open, LOS passes through the corner. Evaluate each crossed corner independently, including where a Wall or Door ends exactly at the corner. Two blocking features on the same flank do not close the opposite passage.

Merely touching the corner of a single LOS-blocking Cell or the endpoint of a single LOS-blocking Edge does not by itself block LOS when the other corner passage is open. A ray entering a blocking Cell's interior remains blocked by the ordinary terrain/fixed-object rule. Corner passages use LOS-blocking properties, not movement passability, and do not bend the straight sightline. Apply established query-specific exceptions (including FoW blocking-destination discovery, Phase vision's ignored Walls/Doors, and other explicit source/target exemptions) before evaluating whether a passage is closed. Unit figures retain their established interior-only LOS-blocking rules and are not treated as flank terrain or fixed objects. This clarification does not change movement or Phase traversal.

Friendly Units do not block LOS.

Hostile Units block LOS when the interior of any Cell in their footprint lies between the observing/acting Unit's source Cell and the target Cell. Friendly Units do not block LOS. When evaluating LOS to a target Unit, exclude the target Unit's complete footprint as a blocker; it cannot block LOS to itself. Likewise, the observing/acting Unit's own complete footprint cannot block its own LOS. These exemptions are by Unit identity, not merely by endpoint Cell.

This rule applies to normal Unit-origin Line of Sight, including Normal Attack and Telekinesis. For selection of Fireball's target Cell, if that Cell is occupied by a hostile Unit, that entire Unit is likewise excluded as a blocker for LOS to that target Cell. Rules that establish LOS from something other than a Unit, such as Fireball's explosion-origin Cell, define whether Unit figures participate in that LOS check.

Which terrain types, fixed objects, walls, doors, windows, and other edge features block LOS will be defined by their corresponding rules.

### Attack and defence dice

When an Attack begins resolving, its target set and the dice counts used for that Attack are determined from the Game State at that point, before any dice for the Attack are rolled. Determine the authoritative number of Attack Dice and the effective number of Defence Dice for each target before resolving any of those rolls.

Those dice counts remain fixed for the remainder of that Attack. State changes caused while resolving the Attack do not change the Attack Dice or Defence Dice already determined for later targets of the same Attack. Dice rolls, Damage, death, and other automatic outcomes may still be resolved sequentially, with their resulting state changes represented normally.

This is a snapshot of the Attack's targets and dice counts, not a snapshot of the whole Game State and not simultaneous Damage resolution. Once that Attack has fully resolved, its state changes affect subsequent Attacks normally.

The attacker rolls a number of Attack Dice equal to the authoritative `ATK` for that specific Attack, including any applicable target-specific modifiers such as Backstab.

An Attack rolls its Attack Dice **once**, regardless of how many Units it targets. If an Attack has multiple targets, the same Attack Dice result and Hit count are used against every target. Each target still rolls its own snapshotted Defence Dice separately, and Damage is determined separately for each target. This shared Attack roll is the normal multi-target Attack rule rather than a Fireball-specific exception.

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

### Defeat

A Unit is **defeated** when an unreplaced rules outcome defeats it. Ordinary lethal Damage causes defeat when the Unit's current HP would otherwise reach zero.

Before defeat occurs, apply any applicable replacement effect. If a replacement such as Undying applies, the replaced defeat does not occur and no consequence that requires the Unit to have been defeated is created.

When defeat does occur, the Unit leaves play and its Figure is removed from the Board as one rules event. There is no playable intermediate state in which a defeated Unit remains on the Board. Retained digital records do not mean that the Unit remains in play.

A `When this Unit is defeated` consequence is created by that defeat event and follows the automatic-consequence queue rules. It may use the Unit's relevant state and footprint from the moment it was defeated even though its Figure has already been removed when the consequence later resolves.

This rule applies equally to Heroes and Monsters. **Defeat** is the rules term for this lifecycle outcome; **remove** describes taking a Figure off the Board and is not a synonym for defeat, because future rules may remove a Figure for another reason.

## Loot, Items and Equipment V1

Loot is a mechanism for generating random rewards and effects. Items and Equipment are individually owned objects whose ownership is independent of Loot generation: scenarios may supply them directly. Immediate results resolve without becoming owned objects. Resources retain their existing separate transport and storage rules.

### Drops and unopened Loot

Each Unit Type may specify a d6 drop chance from 0 through 6 success faces. Actual defeat may create one unopened Loot marker at the defeated Unit's former anchor Cell according to that chance. Drops do not depend on the killer, Side or active Unit. A replaced defeat, including Undying, creates no drop.

A scenario specifies a finite Loot Deck composed of individual cards. An unopened marker has no assigned card identity; determine its card by drawing at pickup. Loot is separate from the World Deck. Multiple unopened markers may occupy one Cell, alongside known objects and other Cell contents. Loot does not block movement or LOS and does not reserve occupancy. These markers are distinct from the existing Cell Marker category and its one-marker-per-Cell restriction.

All Units automatically collect unopened Loot at their completed movement or placement destination, including Stay and external relocation. Resolve existing movement hazards and consequences before pickup. Pickup is mandatory regardless of ownership capabilities. Each unopened marker causes exactly one card draw, and that card and its automatic consequences resolve completely before another pickup.

After each card, continue collecting only while the collecting Unit remains alive and its current position contains unopened Loot. If an effect moves it, continue at its new position; uncollected markers at the previous position remain. Defeat stops that Unit's pickup. Newly generated drops must not recursively collect themselves.

For multi-Cell Units, pickup includes every occupied Cell, processed top-to-bottom, then left-to-right. Re-evaluate the surviving collector's current footprint after each marker and its consequences. For simultaneous arrivals, resolve all arrival hazards first, then process the acting Unit before other affected Units in destination anchor board order. Optional known-object pickup is legal from any occupied Cell.

### Card categories and ownership

- **Immediate:** Resolve the mandatory effect with the collecting Unit as actor. V1 Immediate effects require no new player choices, target selection, Attacks or nested activations. They may cause Damage, healing, Posture changes or deterministic movement, including negative outcomes.
- **Single Use Item:** Assign the individual object to the collector if its Unit Type has **Can Have Items**; otherwise place it face-up as a known Item on the current Cell. Use consumes that object and uses existing Action or Bonus Action opportunities. Items may provide temporary modifiers or extra Actions without adding activation phases.
- **Equipment:** Assign the individual object to the collector if its Unit Type has **Can Have Equipment**; otherwise place it face-up as known Equipment on the current Cell. Its persistent effects become active immediately under the existing rules, including Posture. Equipment may grant capabilities, stat modifiers or Actions. Identical Equipment effects must not multiply unintentionally.

Can Have Items and Can Have Equipment are independent Unit Type capabilities. Neither is required to trigger unopened Loot. Ownership belongs to individual Units, including individual members of a Monster group. Equipment affects only its bearer; shared base statistics and group activation are unchanged.

Known Items and Equipment may coexist with unopened Loot. Picking up a known object is optional: an active, Upright Unit on its Cell with the corresponding ownership capability may collect it as a Free Action. On actual defeat, all unconsumed Items and Equipment belonging to the defeated Unit become known ground objects at its former anchor Cell.

V1 has no equipment slots, equip/unequip actions, weight or inventory capacity limits, voluntary dropping, transfer, trading, selling, crafting or campaign carryover. Per-item charges, durability and binding are deferred.

### Initial test cards

These cards exercise the system and do not establish a final balanced catalog. Their names are distinct from any same-named World Cards.

| Card | Category | Effect |
| --- | --- | --- |
| Pocket Breakfast | Immediate | Restore 2 HP to the collecting Unit, without exceeding its maximum HP. |
| Second Wind | Single Use Item; Bonus Action | Gain +1 Action this activation. |
| One-Size-Fits-Most Bedsheet | Equipment | Grants Phase. |
| Lucky Boots | Equipment | MOV +1. |
| Official-Looking Key | Equipment | Grants Open Door. |
| Emergency Goat Bell | Equipment | Grants an unlimited-use Action: stand up one adjacent friendly Lying Unit. |

Pocket Breakfast's healing is mandatory and capped by maximum HP; it does not offer a choice of healing quantity. Existing meanings of Phase, Open Door, adjacent, friendly, Posture, Action and Bonus Action apply. The Goat Bell spends an ordinary Action on each use and has no per-item usage counter.

### Setup, physical representation and observation

Scenarios may configure Loot Deck composition, Unit Type drop chances and ownership capabilities, initial unopened Loot markers, known ground objects and initial individual inventories. Existing scenarios without Loot retain their existing gameplay.

Physical play uses dice for drops, unopened markers on the board and cards drawn at pickup. Keep known ground object cards associated with their Cells and owned cards in the owning Unit's inventory area. Use numbered identity markers and matching inventory areas to distinguish individual group members when necessary; a lone identifiable Unit may use its ordinary Unit card. Resolve cards one at a time and move owned cards to the former anchor Cell when their bearer is defeated. This ownership must remain recoverable from physical components at stable boundaries.

Authoritative state, save/resume and replay preserve individual ownership and Loot resolution. Fog of War applies to Loot, ground objects, ownership and effects under the existing observation and event-time visibility rules. Hidden draws and inventories must not disclose card identities. Designer authoring remains omniscient.

### Live Loot configuration

New scenarios use the centrally maintained Classic Loot Deck by default. Classic, Fun, Experimental and Far Out are live references to central card-ID/quantity compositions, resolved when a new game is created. Custom stores only its own card-ID/quantity composition, never card rules. Copy to Custom copies the preset's current quantities and removes the live preset reference. An empty Custom deck disables random Loot drops. Existing no-Loot files and drafts migrate to empty Custom decks; explicit legacy compositions retain their cards and quantities.

Unit Types have canonical d6 drop defaults. Scenario overrides are optional per Unit Type; absence inherits its current definition, whereas explicit zero disables drops. Existing authored Unit Type values retain precedence over canonical content, and explicit scenario Loot overrides take precedence over those values. New Loot-only overrides do not copy Unit Type rules. Summoned Units use the same drop probabilities as other Units of their Type. Farming is allowed: additional enemies and activation pressure create risk, and the finite deck limits total rewards. Balance is evaluated through play, without a special summoned-Unit exclusion.

Preset changes affect new games, not an in-progress game's remaining finite deck. Save/resume retains the actual remaining draw pile and reservations without resolving or replenishing the preset. Card rules remain central. Generator recipes retain the selected live preset and never expand it into Custom cards.

A scenario may specify a positive integer multiplier for a live Loot preset. At setup, multiply every current preset card quantity by that same integer, preserving its relative distribution. The resulting deck remains finite and is never replenished or rescaled during gameplay. Copy to Custom copies the resolved, multiplied quantities. Procedural generation may select a multiplier to supply initial unopened markers and expected defeat drops; canonical drop probabilities, including those of summoned Units, are unchanged.

Initial canonical drop values are 1/6 for ordinary Units, 2/6 for stronger or specialist Units, and 3/6 for the Red Dragon, as authored explicitly in the roster. The four canonical Heroes can have Items and Equipment so that default generated parties can use their rewards. Scenario capability overrides remain available and independent.

### Finite supply

Every unopened Loot marker reserves one available card in the finite draw pile, without assigning its identity. The number of unopened markers must never exceed the number of available draw cards. Reject initial scenarios that violate this invariant. A defeat cannot create a marker when all remaining draw cards are already reserved by unopened markers.

Drawn cards never become available again, whether resolved, owned, discarded, consumed or lying on the ground as known objects. There is no automatic reshuffle. Initial known objects and inventories do not contribute available draw cards. Attempting pickup with an empty draw pile is an invariant violation, not an empty reward or permission to remove a marker silently.

## Physical component constraints

The abstract game model and the available physical components are separate concerns.

The rules may permit a world configuration that cannot be constructed using the currently manufactured set of terrain pieces.

For example, a wall edge may be valid in the abstract model, while no physical water tile with an attached wall exists. With the current component set, that means such a map configuration cannot be physically constructed.

Component availability should therefore constrain physical scenario construction rather than silently becoming a universal game rule.

This separation also allows the physical component set to grow without requiring fundamental rule changes.

## Loose tokens and markers

Resources are the initial loose Board markers, as defined under **Resources and Transport Resource**. A Resource marker has a type and occupies one Cell without affecting the Unit or terrain occupancy rules. No generic loose-token framework is required by this rule.

## Procedural scenario generation — first profile

Procedural generation is an authoring/setup operation, not a new gameplay-resolution mechanic. A generator accepts explicit parameters and an optional seed and produces an ordinary `ScenarioDefinition` governed by the existing engine rules. The first profile is **Heroes vs Monsters**, using the four standard Heroes and an explicit pool of conventional fantasy Monsters; unrelated content such as vehicles and industrial or chess Units is excluded. The dungeon generator may use Solid Rock as unexcavated terrain, ordinary floors for rooms and corridors, and normal Walls and Doors. Its intended regions must be connected by normally traversable passages, allowing opening of Doors; this geometric guarantee does not imply that every generated game can be won.

Generation configuration includes dungeon geometry, connectivity, Doors, Monster density and difficulty, Objective selection, and optional Hero Fog of War (enabled by default). Difficulty influences Monster selection and is distinct from density. Threat ratings and generation weights are authoring metadata, not Unit Stats or combat rules. Monster FoW is disabled for this profile. The generation algorithm and its tuning are implementation/content policy, not universal core gameplay rules.

The profile supports **Eliminate Enemies** and **Reach Area** Objectives using the existing authoritative Objective types. For Reach Area, the player can choose **all initial Heroes** or **at least X initial Heroes**, where X is a positive count no greater than the initial party size. The Heroes must qualify simultaneously under the existing Reach Area rules. The Monster Side's Objective is **Defeat Units** targeting the initial Heroes: defeat at least one when all initial Heroes must reach the Area, or defeat at least `initialHeroCount - X + 1` when X Heroes must reach it. These are generated scenario configurations, not changes to Objective evaluation. Reach Area outlines may be displayed over unexplored Cells as mission information, without revealing underlying terrain or contents or changing FoW exploration or legal decisions.

A versioned **generation code** records parameters and, when complete, a seed. A code without a seed selects a fresh seed for that generation; the resulting complete code identifies its generated initial scenario for that generator version. The generator must be deterministic for a fixed version, configuration, and seed. A generated scenario can also be exported as ordinary **DELVE1**: this captures the exact concrete scenario and is the preferred artifact for preserving a particularly good map for a campaign. Generator codes are reproducible recipes, not a promise of perpetual backward compatibility across future generator implementations or content changes. Changes to output-affecting generator behavior require a distinct generator version rather than silently changing an existing version's meaning.

Preview is explicitly omniscient authoring; direct play from a prefix or complete code must enter the normal FoW-filtered gameplay view without first revealing the generated layout. Campaign recipes, parameter randomization, sub-scenarios, hierarchical seeds, and runtime map expansion are deferred.

## Fog of War V1 — Digital only*

Fog of War is an optional scenario setting per Side (`fogOfWarSides`). It is **Digital only*** under the existing digital-only exception: permanent exploration is persistent digital game state without a required physical representation. Ordinary physical content and scenarios without Fog of War retain their existing rules. V1 has only permanent exploration; no additional modes are established.

Each in-play Unit of an enabled Side, including Lying Units, contributes vision from every occupied Cell of its footprint. Sight range is a constant 5 Cells for all Units, measured by Manhattan distance, independent of RNG and temporary attack modifiers. There is no separate Sight stat or per-Unit vision override. Vision uses existing geometric LOS and corner rules, with only terrain and edges blocking it; Units do not block vision. The blocking destination Cell can itself be discovered. Phase Units see through Walls and Doors, while other sight-blocking terrain still applies. Phase does not change ordinary attack LOS.

Initial exploration is calculated after the complete scenario has been instantiated. Exploration updates when authoritative changes affect vision, including movement, repositioning, swaps, creation/removal, capabilities, Doors, Edges and terrain. Once explored, a Cell remains explored for the entire game and always displays its current contents. Normal atomic Moves accumulate swept sight from actual canonical transit positions; leaps and other movement without ordinary traversed Cells reveal no invented intermediate positions. Movement remains atomic and creates no intermediate decisions.

Every Decision Provider controlling an enabled Side uses the same authoritative restricted legal decision space. Ordinary path search can traverse and stop only where every footprint Cell is explored, including with Phase; preserve movement patterns, occupancy, corners and deterministic canonical paths. Unknown Units, Resources, Machines, Markers and Doors cannot be explicitly selected. Legal area actions still resolve against the complete world and may affect hidden Units; previews omit hidden automatic recipients. An authored Move To Order may name an unexplored destination without revealing it, but execution uses normal restricted movement and may make no progress. V1 grants no controller-specific knowledge privileges or new exploration Behavior.

Activation Bag rules are unchanged: populate and draw every normal token. Hidden Units continue acting, subject to their own Side's Fog of War setting. There are no sleeping regions, inactive Units or placeholder hidden-activity events.

A Side-specific observation preserves known board dimensions. Unexplored Cells are explicitly Unknown and reveal no terrain or contents. Edges bordering at least one explored Cell may be shown; Edges between two unknown Cells remain hidden. A multi-Cell Unit is observable in full when any occupied Cell is explored. Units entering unknown territory disappear; Units entering explored territory appear. A stable viewing Side is used, independent of the active token. The Scenario Designer remains omniscient.

Events entirely outside the Side's event-time exploration are omitted without placeholders, including hidden activations, movement and combat. Visible consequences may be shown without disclosing hidden sources; individual targets and details are filtered for partial visibility. Event history, snapshots and animation must correspond using exploration at event time, not final exploration. Bag presentation is generic and cards, Orders, decisions, status and commentary must avoid ordinary-play spoilers.

At the start of a direct Attack, reveal its established qualifying trajectory, including endpoints, to an enabled Side when the attacker belongs to that Side or the direct target Unit/Cell is already observable to that Side. Evaluate observability before revealing the trajectory. Reveal it even on a miss. Hidden combat with no observable endpoint remains hidden. Use the existing attack geometry and the actual qualifying line; do not invent paths for area effects without an established direct trajectory. This does not permit blind firing or alter normal attack LOS.

Exploration belongs to authoritative Game State, survives serialization/continuation and is deeply copied for hypothetical queries. Restart creates fresh initial exploration. Scenario exports contain configuration, never runtime exploration history. Existing JSON and DELVE1 scenarios remain compatible.

This is a gameplay presentation feature aimed at enjoyable exploration and avoiding obvious spoilers. Anti-cheat measures, timing side channels, traffic analysis, developer-tools inspection, deliberate inference from request timing/sizes, cryptographic secrecy and adversarial information-flow analysis are outside V1. Initial explored-area painting, additional modes and a separate Sight stat are not part of V1.

## Explicitly deferred

The following are intentionally not specified yet:

- terrain effects beyond the currently defined Passable and Blocks LOS properties,
- LOS effects beyond the currently defined terrain and edge properties,
- stat modifier rules beyond the currently defined additive `ModifierThisTurn(stat, amount)` behavior,
- edge effects beyond the currently defined Passable and Blocks LOS properties and the Open Door action,
- other loose tokens and markers beyond Resources, established Cell Markers, Loot and known Items/Equipment,
- exact terrain and edge-feature taxonomies,
- scenario validation against an inventory of physical components.

These should be introduced only when the corresponding game rules or content require them.
