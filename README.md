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

`UnitType.Actions` supplies Normal Attack and Open Door. `UnitType.Hero(...)` includes both; the ordinary constructor defaults to Normal Attack. `UnitType.TryOpenDoor` supplies a reusable parameterized Action with a success count from 0 to 6. It rolls one ordinary D6 through `IRandomProvider.RollD6()`; faces 1 through the success count succeed. Every attempt consumes Act and emits `DoorOpeningAttemptResolved` with the roll and outcome; success also emits the normal `DoorOpened` event. `UnitType.Behaviors` can include `ApproachThroughClosedDoors`. DefaultMonsterProvider interprets this preference by passing `closedDoorsTraversable: true` to the shared approach-distance query for both movement destinations and staying. The query defaults to treating Closed Doors as impassable and does not infer traversal policy from Unit Type identity or behavior. Actual movement and LOS still treat Closed Doors normally. Other providers, including human control, may ignore the Behavior. `UnitType.Zombie()` composes MOV 2, Melee, ATK 3, DEF 3, HP 1, Normal Attack, TryOpenDoor(2/6), and that Behavior. DefaultMonsterProvider prefers Normal Attack, then TryOpenDoor, ranking doors by the opposite cell in top-left order. It does not choose ordinary Open Door.

The first scenario uses 1×1 figures on Floor cells. Floor is enterable and does not block LOS. Movement respects walls, closed and open doors, friendly pass-through, hostile blocking, and the top → left → right → bottom shortest-path tie-break. Normal attacks use the defined Melee/range rules, Attack and Defence Dice, damage, death, and figure removal. A Random Provider returns the die symbols; its physical implementation must preserve the Attack Die's 3 Hit/3 Miss and Defence Die's 2 Block/4 Miss face distributions. `SideId` determines friendship independently of Unit Type or who supplies decisions. Open doors do not block LOS, including corner passages. The engine rejects attack LOS involving other featured edges or an intervening hostile Unit, whose LOS effects are still undecided.

Run the automated tests with `dotnet test ProjectDelve.sln`. The engine targets .NET 10 and remains independent of UI and persistence. The immediate goal remains to use executable scenarios to expose rule ambiguities and evolve from concrete game content.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to manually play successive Rounds on a hard-coded 6?5 Floor board with one Hero and two Monsters sharing a Unit Type and activation token. Hero decisions use numbered engine-supplied choices. Monster unit selection, movement, and attacks use DefaultMonsterProvider automatically; token draws and dice are automatic. The host displays the board, Unit stats and HP, activation state, pending decisions, movement paths, and resulting rules events. ASCII edges show walls as `#`, closed doors as `D`, and open doors as `o`; blank edges are open. Walls and the closed door are impassable under the existing movement rules. Move the Hero from `(4,2)` to `(3,2)` and choose Open Door during Act to open the existing door. The board then shows `o`, and a subsequent Move can cross to `(2,2)` using the existing OpenDoor movement rules.

All Units have `MOV 2`. The Hero starts at `(4,2)`, M1 at `(1,2)`, and M2 at `(1,3)`. A barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2. Rows 0 and 4 provide open Floor routes around its ends. Two additional walls above and below `(2,2)` make that cell a dead end entered only from the left.

To observe the third default movement rule, choose `0` to keep the Hero still and skip attacks when offered, and press Enter between Rounds. Neither Monster can reach an attack position in its first Move. M1 chooses `(1,2) -> (1,1) -> (1,0)`, leaving 3 traversable steps to an attack position such as `(3,1)`. Moving right to `(2,2)` would put it geometrically closer to the Hero, but leave 6 traversable steps because it must backtrack out of the dead end. Other two-step destinations, including `(2,1)` and `(1,4)`, also leave 3 steps; top-left board order selects `(1,0)`. M2 then chooses `(1,3) -> (2,3) -> (2,4)`, leaving 2 steps to `(3,3)` around the lower end. Remaining traversable distance ranks first, movement length this activation second, and top-left board order third. MovementCompleted events show the actual canonical paths. With the Hero still at `(4,2)`, M2 reaches an attack position in Round 2 and M1 in Round 3, provided the Hero is still alive. AttackResolved events show attacks; Monsters already able to attack stay in place.

Choose `0` to stay still or take no action when offered, or `q` to quit. The engine automatically resolves choices with only one legal alternative, including selecting a sole eligible Unit. Bonus Action currently completes without an ability choice. With both Monsters in play, their group selects Units in current top-left board order in each phase. Legal attack targets are ranked by Manhattan distance, then top-left board order. Movement follows the existing canonical paths; occupied cells cannot be destinations. After each Round, press Enter to start the next Round using the resulting state, or `q` to quit. HP, figure positions, and deaths persist; the engine rebuilds the activation bag from Unit Types with living Units. Monster activations progress without manual decisions until a Hero decision is required or the Round completes. There are no victory conditions in this host.

## Browser playtest host

Run `dotnet run --project ProjectDelve.Web -- --urls http://localhost:5080`, then open [localhost:5080](http://localhost:5080).

The Web host serves a plain HTML/CSS/JavaScript client and a hard-coded 10×8 Floor encounter. One game lives in memory for the lifetime of the host; restarting the host resets it. Click **Start round**, then select from the engine-supplied choices for either Hero. Monster choices use `DefaultMonsterProvider` automatically. Completed rounds wait for **Start next round**. There are no victory conditions. The Console retains its original 6×5 scenario.

Aria starts at `(1,2)` on the west side; Bram starts at `(8,5)` on the east side. Each has its own Unit Type and activation token, and both have Normal Attack and Open Door. Three Wolves and two Sentinels occupy the central area and share one activation token per Monster Type. Wolves have MOV 3 and Melee; Sentinels have MOV 2 and RNG 2. Both use the existing default behavior. All Monsters have HP 1; both Heroes have HP 4, ATK 2, DEF 1, and MOV 3. Aria has Melee and Bram has RNG 2.

Short barriers between columns 3/4 and 6/7 leave routes around their ends. Four closed doors offer shortcuts, an initially open eastern door offers another entry, and a short central divider varies north/south paths. To try the closest shortcuts, move Aria to `(3,2)` or Bram to `(7,5)` and select **Open door**. Either Hero can also flank around the barriers. The grid shows figures, walls, closed doors, and open doors, with engine-supplied movement destinations highlighted. An opened door does not make an occupied destination legal.

The scenario exercises four activation tokens, two Monster groups resolving Move before Act, multiple hostile targets, route changes after opening doors, and encounters from opposing directions. Default movement seeks an attack position against any hostile; it does not retain a chosen pursuit target. At Act, default behavior selects the nearest supplied legal target by Manhattan distance, then top-left board order. No targeting or movement rules have been added for the larger encounter.

Z1 starts at `(0,6)` in a corridor with a Closed Door between `(0,3)` and `(0,4)` as its only exit toward Aria. Let the Heroes stay to observe Z1 move to `(0,4)` and try the door on its first activation. A failed roll leaves it there to retry on a later activation; a successful roll opens its route. The event log and playback show the D6 roll, success count, success or failure, and Act consumption from domain events.

Events play sequentially: movement follows the engine's canonical path, attacks show the supplied Hits/Blocks/Damage, death removes a figure, and door opening changes the visible edge. HP and round/phase labels synchronize to the authoritative snapshot after playback. **Skip effects** ends playback quickly; uncheck **Animate events** to use immediate rendering. **Refresh** fetches the current snapshot without replaying old events. The browser performs no movement, combat, activation, or Monster rules.

You can choose directly on the board: click a highlighted movement destination, an attack target's figure or cell, or a highlighted door edge. During Move, clicking the current Unit chooses **Stay here** when available and unambiguous. Hover labels identify the choice; highlighted targets also support Tab and Enter/Space. Board controls and the choice buttons submit the same supplied candidate key, and are disabled during requests and playback. The choice panel remains available for debug use and any ambiguous targets.

The HTTP boundary is deliberately small:

| Endpoint | Request | Response / behavior |
| --- | --- | --- |
| `GET /api/game` | None | `{ revision, result }`, with current State/NextInput and an empty Events list; no side effects. |
| `POST /api/game/round` | `{ "expectedRevision": 0 }` | Starts a round and advances automatic Monster decisions to a player choice or round end. |
| `POST /api/game/decision` | `{ "expectedRevision": 1, "candidateKey": "3,2" }` | Resolves the selected engine candidate and advances Monsters to the same stopping boundary. |

Mutation responses use the same `{ revision, result }` envelope. `result` serializes the existing `EngineResult`, with ordered events accumulated across automatic Monster advances. JSON properties are camelCase and enums use their C# names. Candidate keys are opaque; submit the supplied key, or explicit `null` when `AllowsNone` permits it. Both request fields are required. The server retains authoritative state and rejects illegal choices with `400`, or stale revisions and invalid lifecycle operations with `409`. Concurrent mutations are serialized. A failed or uncertain browser submission fetches the current snapshot rather than retrying the decision automatically.

Scenario content and the random provider remain host-local; the Engine and Console have no Web dependencies. The host explicitly routes `aria-type` and `bram-type` to player input, and `wolf-type`, `sentinel-type`, and `zombie-type` to the existing default provider. `ProjectDelve.Web.Tests` exercises the host over loopback HTTP with deterministic randomness, including serialization, illegal/stale decisions, concurrency, all five tokens, both Hero choices, Monster group event order, changed routes after opening both approach doors, attacks against both Heroes, damage, death, and round progression. The full suite runs with `dotnet test ProjectDelve.sln`.
