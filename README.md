# Project Delve

Project Delve is a physical-first fantasy dungeon board game and a software reference implementation of its rules.

The physical tabletop game is the source of truth. The digital implementation exists to make the rules precise, testable, reproducible, and usable by different clients without introducing mechanics that only make sense on a computer.

## Design principles

- **Physical first.** Every game mechanic must be possible to perform and represent in the physical game.
- **Explicit rules.** Software implementation details must not silently become game rules.
- **Shared rules model.** Heroes and Monsters use the same underlying mechanics; their main distinction is who or what makes their decisions.
- **Engine-owned legality.** The rules engine computes legal choices. Human players, Monster Behavior, tests, simulations, or future AI choose only among those legal alternatives.
- **Separate state and presentation.** Physical State, Rules/Game State, and presentation are distinct concerns.
- **Deterministic where appropriate.** Non-random rules resolution should be reproducible. Physical randomness is represented explicitly through replaceable random providers.
- **Do not speculate.** Architecture and extension points should be introduced when actual rules or game content require them.

## Rules and constraints

[CONSTRAINTS.md](CONSTRAINTS.md) is the authoritative living specification for game-design decisions and implementation constraints that have been established so far.

Implementation work should read it before making assumptions about the rules.

If a required behavior is ambiguous or deliberately deferred in `CONSTRAINTS.md`, do not invent a rule merely to complete an implementation. Surface the ambiguity so the game design can be clarified first.

The README intentionally does not duplicate the detailed rules from `CONSTRAINTS.md`.

## Reference implementation

The reference implementation uses **C#/.NET**.

Its core rules engine is intended to be independent of UI, rendering, transport, and persistence technology. The game progresses as a persistent, stepwise state machine that can stop at stable decision points and later resume from serialized state.

Rules resolution produces authoritative Game State together with meaningful Rules/Domain Events. Clients may translate those events into text, animations, sounds, or other presentation without affecting the rules.

Decision Providers and Random Providers form explicit boundaries around agency and physical randomness. This allows the same engine to support human play, Monster Behavior, deterministic tests, simulations, and possible future AI-driven play.

## Current status

Project Delve now has a first executable reference-engine slice in `ProjectDelve.Engine`, with focused tests in `ProjectDelve.Engine.Tests`.

The slice resolves one Round at a time: Unit Type tokens are drawn without replacement, each group completes Bonus Action, Move, then Act, and the engine pauses at legal Unit, destination, and action decisions. State contains the pending decision and can be serialized between steps. The Decision Provider selects from supplied candidates or returns `null` where doing nothing is legal; the Random Provider supplies token draws and physical die faces.

`UnitType.Actions` composes the supported capabilities: Normal Attack and Open Door. Construct Hero content with `UnitType.Hero(...)` to get both by default; the ordinary constructor defaults to Normal Attack for Monster content. A particular Monster Type can gain Open Door by adding that capability without changing its class or side. Act supplies all legal attacks and adjacent closed doors in one request, plus the option to take no action. Each door is a separate choice. Opening it consumes Act, changes the authoritative edge to OpenDoor, and emits DoorOpened with the Unit and edge. DefaultMonsterProvider continues to choose attacks and does not choose door opening.

The first scenario uses 1×1 figures on Floor cells. Floor is enterable and does not block LOS. Movement respects walls, closed and open doors, friendly pass-through, hostile blocking, and the top → left → right → bottom shortest-path tie-break. Normal attacks use the defined Melee/range rules, Attack and Defence Dice, damage, death, and figure removal. A Random Provider returns the die symbols; its physical implementation must preserve the Attack Die's 3 Hit/3 Miss and Defence Die's 2 Block/4 Miss face distributions. `SideId` determines friendship independently of Unit Type or who supplies decisions. The engine rejects attack LOS involving featured edges or an intervening hostile Unit, whose LOS effects are still undecided.

Run the automated tests with `dotnet test ProjectDelve.sln`. The engine targets .NET 10 and has no UI or persistence adapter. The immediate goal remains to use executable scenarios to expose rule ambiguities and evolve from concrete game content.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to manually play successive Rounds on a hard-coded 6?5 Floor board with one Hero and two Monsters sharing a Unit Type and activation token. Hero decisions use numbered engine-supplied choices. Monster unit selection, movement, and attacks use DefaultMonsterProvider automatically; token draws and dice are automatic. The host displays the board, Unit stats and HP, activation state, pending decisions, movement paths, and resulting rules events. ASCII edges show walls as `#`, closed doors as `D`, and open doors as `o`; blank edges are open. Walls and the closed door are impassable under the existing movement rules. Move the Hero from `(4,2)` to `(3,2)` and choose Open Door during Act to open the existing door. The board then shows `o`, and a subsequent Move can cross to `(2,2)` using the existing OpenDoor movement rules.

All Units have `MOV 2`. The Hero starts at `(4,2)`, M1 at `(1,2)`, and M2 at `(1,3)`. A barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2. Rows 0 and 4 provide open Floor routes around its ends. Two additional walls above and below `(2,2)` make that cell a dead end entered only from the left.

To observe the third default movement rule, choose `0` to keep the Hero still and skip attacks when offered, and press Enter between Rounds. Neither Monster can reach an attack position in its first Move. M1 chooses `(1,2) -> (1,1) -> (1,0)`, leaving 3 traversable steps to an attack position such as `(3,1)`. Moving right to `(2,2)` would put it geometrically closer to the Hero, but leave 6 traversable steps because it must backtrack out of the dead end. Other two-step destinations, including `(2,1)` and `(1,4)`, also leave 3 steps; top-left board order selects `(1,0)`. M2 then chooses `(1,3) -> (2,3) -> (2,4)`, leaving 2 steps to `(3,3)` around the lower end. Remaining traversable distance ranks first, movement length this activation second, and top-left board order third. MovementCompleted events show the actual canonical paths. With the Hero still at `(4,2)`, M2 reaches an attack position in Round 2 and M1 in Round 3, provided the Hero is still alive. AttackResolved events show attacks; Monsters already able to attack stay in place.

Choose `0` to stay still or take no action when offered, or `q` to quit. The engine automatically resolves choices with only one legal alternative, including selecting a sole eligible Unit. Bonus Action currently completes without an ability choice. With both Monsters in play, their group selects Units in current top-left board order in each phase. Legal attack targets are ranked by Manhattan distance, then top-left board order. Movement follows the existing canonical paths; occupied cells cannot be destinations. After each Round, press Enter to start the next Round using the resulting state, or `q` to quit. HP, figure positions, and deaths persist; the engine rebuilds the activation bag from Unit Types with living Units. Monster activations progress without manual decisions until a Hero decision is required or the Round completes. There are no victory conditions in this host.
