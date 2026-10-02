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

The slice resolves one Round at a time: Unit Type tokens are drawn without replacement, each living Unit completes its whole activation before the next eligible Unit of its Type is selected from current state. The engine pauses at legal Unit selections and typed activation choices. State contains the pending decision and can be serialized between steps. The Decision Provider selects an opaque supplied key: Move or Stay first, then an Action or End Turn, with applicable Free Actions alongside these choices. Stay completes Move; End Turn finishes the activation. A mandatory MoveAfterAttack continuation resolves before End Turn becomes legal and still allows `null` for staying; the Random Provider supplies token draws and physical die faces.

`UnitType.Actions` defaults to Normal Attack. `UnitType.FreeActions` explicitly supplies Open Door on Barbarian and Rogue; the ordinary constructor and `UnitType.Hero(...)` do not grant it implicitly. Each adjacent Closed Door supplies one Open Door candidate before or after Move and Action. Opening emits `DoorOpened`, consumes no Move, Action, or Bonus Action opportunity, and regenerates choices from the resulting state. There is no Free Action phase or skip choice.

`UnitType.TryOpenDoor` supplies a reusable parameterized Action with a success count from 0 to 6. It rolls one ordinary D6 through `IRandomProvider.RollD6()`; faces 1 through the success count succeed. Every attempt consumes the single Action and emits `DoorOpeningAttemptResolved` with the roll and outcome; success also emits the normal `DoorOpened` event. `UnitType.Behaviors` can include `ApproachThroughClosedDoors`. DefaultMonsterProvider interprets this preference by passing `closedDoorsTraversable: true` to the shared approach-distance query for both movement destinations and staying. The query defaults to treating Closed Doors as impassable and does not infer traversal policy from Unit Type identity or behavior. Actual movement and LOS still treat Closed Doors normally. Other providers, including human control, may ignore the Behavior. `UnitType.Zombie()` composes MOV 2, Melee, ATK 3, DEF 3, HP 1, Normal Attack, TryOpenDoor(2/6), and that Behavior. DefaultMonsterProvider prefers Normal Attack, then TryOpenDoor, ranking doors by the opposite cell in top-left order. It does not choose ordinary Open Door.

The first scenario uses 1×1 figures on Floor cells. Floor is enterable and does not block LOS. Movement respects walls, closed and open doors, friendly pass-through, hostile blocking, and the top → left → right → bottom shortest-path tie-break. Normal attacks use the defined Melee/range rules, Attack and Defence Dice, damage, death, and figure removal. A Random Provider returns the die symbols; its physical implementation must preserve the Attack Die's 3 Hit/3 Miss and Defence Die's 2 Block/4 Miss face distributions. `SideId` determines friendship independently of Unit Type or who supplies decisions. Open doors do not block LOS, including corner passages. The engine rejects attack LOS involving other featured edges or an intervening hostile Unit, whose LOS effects are still undecided.

Run the automated tests with `dotnet test ProjectDelve.sln`. The engine targets .NET 10 and remains independent of UI and persistence. The immediate goal remains to use executable scenarios to expose rule ambiguities and evolve from concrete game content.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to play successive Rounds on a hard-coded 6x5 stone floor board with a Barbarian at `(4,2)` and two Grunts at `(1,2)` and `(1,3)`. The barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2; rows 0 and 4 provide open routes around its ends. Barbarian can open the door as a Free Action. The Console uses the same named definitions as the Web host.

Hero decisions use numbered engine-supplied choices. Choose the supplied Stay or End Turn option, or `q` to quit. An optional follow-up Move still uses `0` to stay. Monster choices use DefaultMonsterProvider; tokens and dice resolve automatically. Positions, HP, deaths, and door state persist between Rounds. The host shows canonical movement paths and resulting rules events. There are no victory conditions.

## Browser playtest host

Run `dotnet run --project ProjectDelve.Web -- --urls http://localhost:5080`, then open [localhost:5080](http://localhost:5080).

The Web host serves a plain HTML/CSS/JavaScript client and one hand-authored 15x15 playtest map. One game lives in memory for the lifetime of the host; restarting resets it. Click **Start round**, then choose for either Hero. Monster choices use `DefaultMonsterProvider` automatically. Completed rounds wait for **Start next round**. There are no victory conditions.

The real roster contains exactly six Unit Types (stats in MOV / RNG / ATK / DEF / HP order):

| Type | Stats | Actions / special content |
| --- | --- | --- |
| Barbarian | 3 / 1 / 4 / 3 / 5 | NormalAttack, OpenDoor (Free Action) |
| Rogue | 4 / 1 / 3 / 2 / 4 | NormalAttack, OpenDoor (Free Action) |
| Grunt | 3 / 1 / 3 / 3 / 1 | NormalAttack only; no special capability or behavior |
| Zombie | 2 / 1 / 3 / 3 / 1 | NormalAttack, TryOpenDoor(2/6), ApproachThroughClosedDoors |
| Skeleton Archer | 3 / 4 / 3 / 3 / 1 | NormalAttack, MaximizeAttackDistance |
| Goblin | 4 / 1 / 2 / 2 / 1 | NormalAttack, MoveAfterAttack(1), BackAwayAfterAttack |

Barbarian (B) starts at `(4,7)` and Rogue (R) at `(4,10)`. Two Grunts, two Zombies, two Skeleton Archers, and one Goblin each share a token with others of their Type. The crypt interior covers `x=1..3, y=2..5`, enclosed by walls except for the Closed Door at `(3,4)-(4,4)`. Z1 at `(2,3)` and Z2 at `(1,5)` must approach and try that door to leave. Let Heroes stay to observe the attempts, including failure/retry or success/opening, or move a Hero beside the door to open it freely.

The narrow passage beside the crypt opens onto a broad courtyard for ranged fire and Goblin movement. A stream has a stone crossing at row 3 and an open route below; the southern ruin has an Open Door and an open end. Sparse trees and tables break up sight and movement without making a maze. A1 starts two cells from Barbarian and can retreat to full firing range; G1 approaches Rogue, attacks, and backs away. Counts are for playtesting, not a balance target.

The responsive layout caps board width by the viewport height on desktop, gives the board a larger column beside the debug panel, and stacks the panel below at narrow widths. Explicit shrinkable grid tracks keep cells square; percentage positioning aligns figures, edges, movement highlights, and click targets. Cell labels, figures, and edge thickness scale with the board. Small cells retain coordinates and terrain tooltips even when their text is compact. No game rules run in JavaScript.

Events play sequentially: movement follows the engine's canonical path, attacks show the supplied Hits/Blocks/Damage, death removes a figure, and door opening changes the visible edge. HP and round/current-Unit labels synchronize to the authoritative snapshot after playback. **Skip effects** ends playback quickly; uncheck **Animate events** to use immediate rendering. **Refresh** fetches the current snapshot without replaying old events. The browser performs no movement, combat, activation, or Monster rules.

You can choose directly on the board: click a highlighted movement destination, an attack target's figure or cell, or a highlighted door edge. During Move, clicking the current Unit chooses **Stay here** when available and unambiguous. Hover labels identify the choice; highlighted targets also support Tab and Enter/Space. Board controls and the choice buttons submit the same supplied candidate key, and are disabled during requests and playback. The choice panel remains available for debug use and any ambiguous targets.

The HTTP boundary is deliberately small:

| Endpoint | Request | Response / behavior |
| --- | --- | --- |
| `GET /api/game` | None | `{ revision, result, autoChooseSingleRelevantChoice }`, with current State/NextInput and an empty Events list; no side effects. |
| `POST /api/game/round` | `{ "expectedRevision": 0 }` | Starts a round and advances automatic Monster decisions to a player choice or round end. |
| `POST /api/game/preferences` | `{ "expectedRevision": 1, "autoChooseSingleRelevantChoice": false }` | Changes relevance-based auto-choice and rebuilds all legal choices; defaults to enabled. |
| `POST /api/game/decision` | `{ "expectedRevision": 1, "candidateKey": "3,2" }` | Resolves the selected engine candidate and advances Monsters to the same stopping boundary. |

Mutation responses use the same `{ revision, result, autoChooseSingleRelevantChoice }` envelope. `result` serializes the existing `EngineResult`, with ordered events accumulated across automatic Monster advances. JSON properties are camelCase and enums use their C# names. Candidate keys are opaque; submit the supplied key, or explicit `null` when `AllowsNone` permits it. All mutation request fields are required. Normal activation choices have explicit Stay/End Turn candidates and do not accept `null`. The server retains authoritative state and rejects illegal choices with `400`, or stale revisions and invalid lifecycle operations with `409`. Concurrent mutations are serialized. A failed or uncertain browser submission fetches the current snapshot rather than retrying the decision automatically.

Every supplied legal candidate carries engine-evaluated `relevant` metadata; current content marks all candidates relevant. The **Filter irrelevant choices (display only)** checkbox hides candidates with `relevant: false` locally, without a server request or any change to submission legality. The separate **Auto-choose single relevant choice** checkbox controls the backend/session preference `autoChooseSingleRelevantChoice`, outside physical/rules GameState. A sole legal candidate with no option to do nothing always resolves automatically, regardless of relevance or this preference. When enabled, the preference additionally auto-selects exactly one relevant candidate among multiple legal candidates in a required choice. When disabled, such multiple-choice requests stop and expose all legal candidates to the provider. An empty optional request still resolves to none; multiple legal choices with no relevant candidates still require external input. Submission validation always uses all authoritative legal candidates. No relevance-sensitive abilities or tactical evaluation are introduced.

Per-unit `MoveDone`, `ActionDone`, and `BonusActionUsed` and the narrow `MoveAfterAttackAllowance` continuation survive serialization. `Pending` is informational: choices are rebuilt from authoritative state on resume. Old saves containing group `Phase` are rejected; no migration is provided.

The host routes `barbarian-type` and `rogue-type` to player input, and the four Monster Types to the existing default provider. The Engine and Console have no Web dependencies. `ProjectDelve.Web.Tests` exercises the host over loopback HTTP with deterministic randomness, including the roster, map dimensions, sealed Zombie room, six tokens, both Hero choices, complete per-Unit Monster event order, Archer retreat and attack, Goblin movement after attack, door attempts, damage, death, stale/illegal decisions, concurrency, and round progression. Engine behavior regressions retain their small test-local Types. Run the full suite with `dotnet test ProjectDelve.sln`.
