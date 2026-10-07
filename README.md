# Project Delve

Project Delve is a physical-first fantasy dungeon board game and a software reference implementation of its rules.

The physical tabletop game is the source of truth. The digital implementation exists to make the rules precise, testable, reproducible, and usable by different clients without introducing mechanics that only make sense on a computer.

## Design principles

- **Physical first.** Every game mechanic must be possible to perform and represent in the physical game.
- **Explicit rules.** Software implementation details must not silently become game rules.
- **Shared rules model.** All Units use the same underlying mechanics. Scenario-authored controller assignments are independent of Side and Unit Type.
- **Engine-owned legality.** The rules engine computes legal choices. Human players, Monster Behavior, tests, simulations, or future AI choose only among those legal alternatives.
- **Separate state and presentation.** Physical State, Rules/Game State, and presentation are distinct concerns.
- **Deterministic where appropriate.** Non-random rules resolution should be reproducible. Physical randomness is represented explicitly through replaceable random providers.
- **Do not speculate.** Architecture and extension points should be introduced when actual rules or game content require them.

## Rules and constraints

[CONSTRAINTS.md](CONSTRAINTS.md) is the authoritative living specification for game-design decisions and implementation constraints that have been established so far.

Implementation work should read it before making assumptions about the rules.

If a required behavior is ambiguous or deliberately deferred in `CONSTRAINTS.md`, do not invent a rule merely to complete an implementation. Surface the ambiguity so the game design can be clarified first.

The README intentionally does not duplicate the detailed rules from `CONSTRAINTS.md`.

The current implementation emits `UnitDefeated` for unreplaced defeat and retains the source Unit, Figure, Unit Type, and occupied Cells as event context. Undying replaces defeat at the shared lifecycle boundary, before any outcome snapshot exposes the result. Existing multi-recipient effects fix their recipients before application, and Attack follow-ups wait for the complete Attack.

No current Ability creates an automatic `When` consequence. FIFO queue draining, consequences created during draining, and defeat-triggered effects from Lying or removed sources therefore have no concrete content to resolve or exercise yet. No trigger framework or placeholder Ability is included. When such content is established, its consequences must finish after the current effect, drain in FIFO order, and finish before `After` follow-ups are offered, using retained defeat context where needed.

## Reference implementation

The reference implementation uses **C#/.NET**.

Its core rules engine is intended to be independent of UI, rendering, transport, and persistence technology. The game progresses as a persistent, stepwise state machine that can stop at stable decision points and later resume from serialized state.

Rules resolution produces authoritative Game State together with meaningful Rules/Domain Events. Clients may translate those events into text, animations, sounds, or other presentation without affecting the rules.

Decision Providers and Random Providers form explicit boundaries around agency and physical randomness. This allows the same engine to support human play, Monster Behavior, deterministic tests, simulations, and possible future AI-driven play.

## Current status

Project Delve now has a first executable reference-engine slice in `ProjectDelve.Engine`, with focused tests in `ProjectDelve.Engine.Tests`.

The slice resolves one Round at a time: `ActivationToken(TypeId, SideId)` values are drawn without replacement; each living Unit completes its whole activation before the next eligible Unit of that exact group is selected from current state. The engine pauses at legal Unit selections and typed activation choices. State contains the pending decision and can be serialized between steps. The Decision Provider selects an opaque supplied key: Move or Stay first, then an Action or End Turn, with applicable Free Actions alongside these choices. Stay completes Move; End Turn finishes the activation. A mandatory MoveAfterAttack continuation resolves before End Turn becomes legal and still allows `null` for staying; the Random Provider supplies token draws and physical die faces.

`GameState.Bag` is a list of immutable `(TypeId, SideId)` records. Round-start construction preserves Type authoring order and sorts each Type's participating Sides ordinally. `ActiveToken` and every decision's `Token` retain that pair. Each participating group requires an explicit controller assignment. Summoning uses an existing group assignment or inherits its source's controller; it never adds a token to the current round's bag.

`AttackInProgress` contains a narrow immutable `AttackContinuation`: source Action, selected Cell, ordered fixed targets and DEF counts, fixed ATK count, stage/index, shared rolled faces/Hits and completed target results. `DoorInProgress` retains the acting Unit, Door, success count and Action id. Costs are spent at commitment, before any roll. Each nonempty pool becomes one mandatory `DecisionKind.RollDice` with the opaque `roll-dice` candidate and result-free `DicePool` metadata. Only submitting that candidate invokes `IRandomProvider`; refresh and invalid/null input cannot roll. Defence belongs to each defender's controller. The committed Attack finishes even if its caster dies or becomes Lying, then qualifies follow-ups.

`RoundStarted`, `ActivationStarted`, `ActivationCompleted`, `ActionUsed`, `AttackStarted` and `DiceRolled` extend the existing RulesEvent stream. Creation, posture, damage and defeat outcomes carry source context where available. Every event still gets a detached `ResolutionStep.StateAfter`; no event sourcing or client-side state reconstruction is involved. `AbilityUsed` remains during transition for existing bonus-use consumers. Browser controls and dice text consume the generic projection; automated hosts submit the same candidate and no browser auto-roll preference is implemented.

The canonical Unit content is declared together in [UnitRoster.cs](ProjectDelve.Engine/UnitRoster.cs). Its typed C# helpers are in `UnitAuthoring.cs` and translate into the existing runtime `UnitType` model. Normal Attack is implicit; `CantAttack()` removes only that Action. Limited uses and mechanic balance parameters are explicit in the roster, and automated Behaviors are attached separately. Existing `UnitType.Barbarian()`, `UnitType.Goblin()`, and other factories forward to this roster and retain custom-ID support. Named card content uses `Ability(name, Uses(n), mechanic)` or `Ability(name, Unlimited(), mechanic)`; the mechanic determines timing. Bonus abilities retain explicit stable ids separate from printed names. Shaman uses `SummonAdjacent(UnitTypeIds.Goblin, Posture.Lying)` and the independent `UseSummon` selection Behavior. Summoning reuses the configured Unit Type already in the game or its canonical roster definition, and descriptions read the same type/posture configuration as resolution.

`UnitType.Actions` defaults to Normal Attack. `UnitType.FreeActions` explicitly supplies Open Door on Barbarian and Rogue; the ordinary constructor and `UnitType.Hero(...)` do not grant it implicitly. Each Closed Door bordering an occupied Cell supplies one Open Door candidate before or after Move and Action. Opening emits `DoorOpened`, consumes no Move, Action, or Bonus Action opportunity, and regenerates choices from the resulting state. There is no Free Action phase or skip choice.

`UnitType.TryOpenDoor` supplies a reusable parameterized Action with a success count from 0 to 6. It rolls one ordinary D6 through `IRandomProvider.RollD6()`; faces 1 through the success count succeed. Every attempt consumes the single Action and emits `DoorOpeningAttemptResolved` with the roll and outcome; success also emits the normal `DoorOpened` event. `UnitType.Behaviors` can include `ApproachThroughClosedDoors`. DefaultAutomatedProvider interprets this preference by passing `closedDoorsTraversable: true` to the shared approach-distance query for both movement destinations and staying. The query defaults to treating Closed Doors as impassable and does not infer traversal policy from Unit Type identity or behavior. Actual movement and LOS still treat Closed Doors normally. Other providers, including human control, may ignore the Behavior. `UnitType.Zombie()` composes MOV 2, Melee, ATK 3, DEF 3, HP 1, Normal Attack, TryOpenDoor(2/6), and that Behavior. DefaultAutomatedProvider prefers Normal Attack, then TryOpenDoor, ranking doors by the opposite cell in top-left order. It does not choose ordinary Open Door.

The first scenario uses 1×1 figures on Floor cells. Floor is enterable and does not block LOS. Movement respects walls, closed and open doors, friendly pass-through, hostile blocking, and the top → left → right → bottom shortest-path tie-break. Normal attacks use the defined Melee/range rules, Attack and Defence Dice, damage, defeat, and figure removal. A Random Provider returns the die symbols; its physical implementation must preserve the Attack Die's 3 Hit/3 Miss and Defence Die's 2 Block/4 Miss face distributions. `SideId` determines friendship independently of Unit Type or who supplies decisions. Open doors do not block LOS, including corner passages. The engine rejects attack LOS involving other featured edges or an intervening hostile Unit, whose LOS effects are still undecided.

Run the automated tests with `dotnet test ProjectDelve.sln`. The engine targets .NET 10 and remains independent of UI and persistence. The immediate goal remains to use executable scenarios to expose rule ambiguities and evolve from concrete game content.

## Exploratory console host

Run `dotnet run --project ProjectDelve.Console` to play successive Rounds on a hard-coded 6x5 stone floor board with a Barbarian at `(4,2)` and two Grunts at `(1,2)` and `(1,3)`. The barrier between columns 2 and 3 has walls in rows 1 and 3 and a closed door in row 2; rows 0 and 4 provide open routes around its ends. Barbarian can open the door as a Free Action. The Console uses the same named definitions as the Web host.

Hero decisions use numbered engine-supplied choices. Choose the supplied Stay or End Turn option, or `q` to quit. An optional follow-up Move still uses `0` to stay. The authored Grunt group uses DefaultAutomatedProvider. Tokens draw automatically; the Barbarian controller confirms each owned dice pool through the numbered Roll Dice option. Positions, HP, deaths, and door state persist between Rounds. The host shows canonical movement paths and resulting rules events. There are no victory conditions.

## Browser playtest host

Run `dotnet run --project ProjectDelve.Web -- --urls http://localhost:5080`, then open [localhost:5080](http://localhost:5080).

The Web host serves a plain HTML/CSS/JavaScript client and six server-owned playtest setups. One authoritative game lives in memory. **Start selected** replaces it with the selected scenario; **Restart current** recreates the current scenario. Both discard the previous state, including pending activations and follow-ups. During effects, either control skips playback and applies the replacement after any in-flight request finishes. Revision checks still protect concurrent clients.

| Scenario id | Name | Board | Initial content |
| --- | --- | --- | --- |
| `basic-combat` | Basic Combat | 8x8 | Barbarian; several Grunts |
| `goblins` | Goblins | 10x10 | Barbarian, Rogue; Grunts and Goblins |
| `archers` | Archers | 12x12 | Barbarian, Rogue; Grunts, Goblins and Skeleton Archers |
| `wizard-doors` | Wizard / Doors | 15x15 | Barbarian, Rogue, Wizard; Goblins, Skeleton Archers, Zombies and a Ghost |
| `full-party-trolls` | Full Party / Trolls | 15x15 | All four Heroes; Goblins, Skeleton Archers, Zombies and Trolls |
| `shaman-hunt` | Shaman Hunt | 15x15 | All four Heroes; Shaman, Grunt and a 2x2 Red Dragon, with no initial Goblins |

Basic Combat is the startup default. Select a setup, click **Start selected**, then **Start round**. The scenarios explicitly assign their current Heroes to human control and their Monsters to `DefaultAutomatedProvider`. Confirm each human-owned Attack, Defence, or door check with **Roll Dice**. Completed rounds wait for **Start next round**. These are test content with no victory conditions or progression. Exact maps and placements are editable definitions in `ProjectDelve.Web/PlaytestScenarios.cs`; the browser receives catalog metadata and authoritative game snapshots.

Scenarios use a separate initial-setup data boundary: `ScenarioDefinition` contains a `BoardDefinition`, ordered `UnitTypeIds`, anchor-based Unit placements, and explicit agency per Unit Type + Side. The board has a default terrain plus sparse Cell overrides and internal Edges identified by `Position + Direction`, with only `Right` and `Down` directions. Placement supports initial Posture and an optional `InitialHp`; normal HP and all ability uses otherwise come from canonical Unit content. Runtime identity and activation progress are not scenario data.

The small `Scenario` authoring helpers expand groups and map partitions into that concrete definition:

```csharp
using static ProjectDelve.Engine.Scenario;

var definition = Define(
    board: Map(8, 8, cells: [Tile(3, 2, TerrainKind.Tree)]),
    groups: [
        Group(UnitTypeIds.Barbarian, "blue", ControllerKind.Human, At(1, 4)),
        Group(UnitTypeIds.Grunt, "red", ControllerKind.Automated, At(4, 4), At(4, 3))
    ]);
var state = GameEngine.CreateGame(definition);
```

`Define` derives normal Type order from first group occurrence; its optional `unitTypeIds` argument preserves explicitly authored order and unplaced Types. `GameEngine.CreateGame` resolves canonical IDs, validates data and normal full-footprint setup legality, and returns independent mutable round-zero state with deterministic generated Unit IDs. It does not draw tokens or invoke providers; `StartRound` still initializes the activation bag and advances play. Catalog presentation remains separate. The console detour and courtyard API fixture use the same creation boundary, including the courtyard's wounded Wizard and unplaced Goblin Type. `ScenarioDefinitionJson` serializes concrete initial setup; `ScenarioDefinitionTransport` wraps that JSON in portable `DELVE1:` strings. Browser imports enter the same creation boundary and restart retains the imported definition.

### Level Designer V1

Click **Designer** in the browser host to author the same `ScenarioDefinition` format visually. The browser retains a local draft, including temporarily invalid placements, and uses only generic grid editing and server-supplied Unit geometry. It does not evaluate Delve placement, terrain, Unique, HP, or agency legality. Reload discards the draft; export a DELVE1 string to keep it. Switching to Playtest and back retains it.

**Terrain** paints sparse overrides by click/drag; **Use default** removes an override. **Edge** paints internal Right/Down boundaries, including empty ones, with Walls, Windows and open/closed Doors; **Remove** erases a segment. **Unit** places the chosen canonical Type at its top-left anchor with arbitrary SideId and an explicit Type + Side controller. **Select** opens cell/edge/Unit properties. Overlapping Units remain editable through a chooser or the placement list. Unit properties include anchor, SideId, Posture and optional InitialHp; blank HP means canonical full HP.

Agency rows are per `(UnitTypeId, SideId)` and can be added, edited or removed independently. Existing pairs are reused on placement; rows with no initial Units are retained. First Type use appends its declaration, deletion does not remove it, imported Type order is preserved, and unplaced Types can be declared explicitly. Order is displayed; V1 does not reorder declarations.

**Validate** checks a snapshot through engine creation. **Export DELVE1** always validates before encoding. **Play design** exports, then uses the existing revisioned `/api/game/scenario/import` operation to create round-zero gameplay; click **Start round** to begin. It does not turn runtime state back into a draft. Errors leave the draft editable, and responses from an older draft/request are ignored. Edits mark it **Unchecked**. No validation requests run during painting.

The Web host limits designer boards and imported playtest boards to **50 × 50** before materialization. This is host/editor policy, not an engine game rule. Shrinking is refused if it would hide authored terrain, edges or any Unit footprint. DefaultTerrain changes retain explicit overrides. Import retains the decoded definition directly, including redundant overrides, explicit None edges, unplaced Types, zero-placement agency, optional HP and all list order.

The designer uses four stateless endpoints: `GET /api/designer/catalog`, `POST /api/designer/import` with `{ transport }`, and `POST /api/designer/validate` / `export` with the ScenarioDefinition JSON body. Catalog metadata comes from the engine's single `CanonicalUnitTypes` registry. Validation returns `{ valid, errors: [{ message, path }] }`; first-error reporting includes authoring locations where available. Import returns scenario JSON; export returns `{ transport }`. These operations never mutate the active game.

Browser tests exercise the actual editor modules using the small Node DOM harness:

```powershell
node --test ProjectDelve.Web.Tests/*.test.cjs
```

For manual acceptance, import a FullParty DELVE1 into Designer; check its 15 × 15 board, four initial party Units and concrete room edges. Paint terrain, change a Door, and select/edit a Unit. Validate, repair any reported issue, export, and click Play design. Confirm round-zero setup, start a round, then return to Designer and confirm the authored draft is retained. Room/line tools, undo, declaration reordering and reload persistence are deferred.

Map authoring uses `Vertical(x, top, bottom, kind)`, `Horizontal(y, left, right, kind)`, and `Room(left, top, right, bottom, kind = EdgeKind.Wall)` to generate complete lines or room sides. Doors and windows are explicit later entries such as `Edge(7, 3, EdgeDirection.Right, EdgeKind.ClosedDoor)`. `Map` applies later entries with the same Position and Direction while retaining the first list position for that identity. `Tiles(TerrainKind.Tree, At(5, 4), At(3, 12))` expands several positions into concrete Cell overrides. The resulting definition contains only concrete Cells and unique Edges; duplicate Edges or Cell overrides in directly constructed definitions remain invalid. Game creation rejects undefined directions, out-of-bounds positions, Right edges in the last column, and Down edges in the last row; there are no exterior-boundary edge definitions.

Across the catalog, the roster includes all twelve current Unit Types (stats in MOV / RNG / ATK / DEF / HP order):

| Type | Stats | Actions / special content |
| --- | --- | --- |
| Barbarian | 3 / 1 / 4 / 3 / 5 | NormalAttack, OpenDoor, Rage, Fury, Cleave |
| Rogue | 4 / 1 / 3 / 2 / 4 | NormalAttack, OpenDoor, Dash, Throwing Knife, Backstab |
| Cleric | 3 / 1 / 3 / 3 / 4 | NormalAttack, OpenDoor, Heal, Holy Wave, Aura |
| Wizard | 2 / 4 / 3 / 2 / 4 | NormalAttack, OpenDoor, Focus, Fireball, Telekinesis |
| Grunt | 3 / 1 / 3 / 3 / 1 | NormalAttack only; no special capability or behavior |
| Ghost | 2 / 1 / 3 / 3 / 1 | NormalAttack, Phase |
| Red Dragon | 2 / 4 / 4 / 4 / 8 | NormalAttack only; Unique; 2x2 footprint |
| Zombie | 2 / 1 / 3 / 3 / 1 | NormalAttack, TryOpenDoor(2/6), ApproachThroughClosedDoors |
| Skeleton Archer | 3 / 4 / 3 / 3 / 1 | NormalAttack, MaximizeAttackDistance |
| Goblin | 4 / 1 / 2 / 2 / 1 | NormalAttack, MoveAfterAttack(1), BackAwayAfterAttack |
| Shaman | 2 / 0 / 0 / 3 / 1 | Summon Goblin, Flee; no Normal Attack |
| Troll | 2 / 1 / 4 / 4 / 1 | NormalAttack, TryOpenDoor(4/6), Undying, ApproachThroughClosedDoors |

Early maps offer movement, flanks, retreat space and broken sight lines. Wizard / Doors encloses a five-by-five room with two gates: Zombies try the Doors while a Ghost near the southwest corner can phase through the Walls toward the Heroes, still needing ordinary LOS to attack. Full Party / Trolls adds another broad gated room and partial partitions, leaving several routes through the central area. Shaman Hunt uses staggered walls, open Doors and obstacles; the Shaman remains reachable and flees through ordinary terrain paths. Its first spawned Goblin begins Lying and enters a later round's bag only through the normal snapshot rules. No scenario adds special rules.

Figures support exactly 1x1 and 2x2 footprints. Position is the top-left occupied Cell, movement paths contain anchors, and a large base remains one Unit for activation, HP and effects. The engine validates every occupied Cell and internal Edge. Shaman Hunt places Red Dragon in its broad eastern area near the partitions, using ordinary Default Monster Behavior without Dragon-specific mechanics.

The responsive layout caps board width by the viewport height on desktop, gives the board a larger column beside the debug panel, and stacks the panel below at narrow widths. Explicit shrinkable grid tracks keep cells square; percentage positioning aligns figures, edges, movement highlights, and click targets. Cell labels, figures, and edge thickness scale with the board. Small cells retain coordinates and terrain tooltips even when their text is compact. Authoritative figure geometry and placement previews come from the browser projection, including progressive snapshots. Any occupied Cell can identify a large Unit, while Cell-targeted choices remain distinct beneath its base. No game rules run in JavaScript.

Events expose token Side, activation start/completion, generic Action use, Attack start and actual dice faces. They play sequentially: movement follows the engine's canonical path, attacks show the supplied Hits/Blocks/Damage, death removes a figure, and door opening changes the visible edge. HP and round/current-Unit labels synchronize to the authoritative snapshot after playback. **Skip effects** ends playback quickly without submitting an unresolved RollDice; uncheck **Animate events** to use immediate rendering. **Refresh** fetches the current snapshot without replaying old events. The browser performs no movement, combat, activation, or Monster rules.

You can choose directly on the board: click a highlighted movement destination, an attack target's figure or cell, or a highlighted door edge. During Move, clicking the current Unit chooses **Stay here** when available and unambiguous. Hover labels identify the choice; highlighted targets also support Tab and Enter/Space. Board controls submit supplied candidate keys and are disabled during requests and playback. The action area shows only choices without a primary board or Unit Card control, including End Turn and ambiguous targets.

The HTTP boundary is deliberately small:

| Endpoint | Request | Response / behavior |
| --- | --- | --- |
| `GET /api/game` | None | `{ revision, result, autoChooseSingleRelevantChoice, scenarioId, scenarios, presentation }`, with current State/NextInput and an empty Events list; no side effects. |
| `POST /api/game/scenario` | `{ "expectedRevision": 0, "scenarioId": "goblins" }` | Discards the old game and creates the selected setup at round 0. Unknown ids return `400`. |
| `POST /api/game/restart` | `{ "expectedRevision": 1 }` | Creates the current scenario from scratch, including Units, ability uses, Doors and empty initial bag. |
| `POST /api/game/round` | `{ "expectedRevision": 0 }` | Starts a round and advances automatic Monster decisions to a player choice or round end. |
| `POST /api/game/preferences` | `{ "expectedRevision": 1, "autoChooseSingleRelevantChoice": false }` | Changes relevance-based auto-choice and rebuilds all legal choices; defaults to enabled. |
| `POST /api/game/decision` | `{ "expectedRevision": 1, "candidateKey": "3,2" }` | Resolves the selected engine candidate and advances Monsters to the same stopping boundary. |

Mutation responses use the same `{ revision, result, autoChooseSingleRelevantChoice, scenarioId, scenarios, presentation }` envelope. `result` serializes the existing `EngineResult`, with ordered events accumulated across automatic Monster advances. JSON properties are camelCase and enums use their C# names. Candidate keys are opaque; submit the supplied key, or explicit `null` when `AllowsNone` permits it. All mutation request fields are required. Normal activation choices have explicit Stay/End Turn candidates and do not accept `null`. The server retains authoritative state and rejects illegal choices with `400`, or stale revisions and invalid lifecycle operations with `409`. Concurrent mutations are serialized. A failed or uncertain browser submission fetches the current snapshot rather than retrying the decision automatically.

Every supplied legal candidate carries engine-evaluated `relevant` metadata. Rage is legal whenever the active Barbarian has uses and Rage unused in this activation. It is irrelevant before Move, after Action, or when no legal Attack can benefit from its modifier; after Move (including Stay), it is relevant when an Attack can benefit. The **Filter irrelevant choices (display only)** checkbox hides candidates with `relevant: false` locally, without a server request or any change to submission legality. The separate **Auto-choose single relevant choice** checkbox controls the backend/session preference `autoChooseSingleRelevantChoice`, outside physical/rules GameState. A sole legal candidate with no option to do nothing resolves automatically, except `RollDice`, which always requires explicit provider submission regardless of relevance or this preference. When enabled, the preference additionally auto-selects exactly one relevant candidate among multiple legal candidates in a required choice. When disabled, such multiple-choice requests stop and expose all legal candidates to the provider. An empty optional request still resolves to none; multiple legal choices with no relevant candidates still require external input. Submission validation always uses all authoritative legal candidates.

Per-unit `MoveDone`, `ActionDone`, and `BonusActionsUsedThisActivation` and the narrow `MoveAfterAttackAllowance` continuation survive serialization. `Pending` is informational: choices are rebuilt from authoritative state on resume. Old saves containing type-only `Bag`/`ActiveTypeId`, group `Phase`, or the shared `BonusActionUsed` flag are rejected; no migration is provided. Each Bonus Action ability may be used once per activation, independently of the others. The activation usage set uses ability names, the existing content identities, and clears at activation end; persistent remaining uses do not reset. Rogue can use both Dash and Throwing Knife in either order, combining MOV +2, RNG +2, and ATK -1.

Barbarian explicitly supplies a `BonusActionAbility` composed of its content name, maximum uses, and `ModifierThisTurn(Atk, 2)`. `UnitType.CreateUnit` initializes its per-Unit `BonusActionUses` to 2/2; bare scenario Units are initialized once by the engine at the first round. The immutable usage value enforces `0 <= RemainingUses <= MaxUses`. Using Rage immediately spends one use and records Rage as used in this activation, even if no Attack follows. `ModifiersThisTurn` belongs to the current activation and is cleared when it ends. `GameState.EffectiveAtkOf` supplies both Attack legality and dice counts; the serialized `effectiveAtk` map supplies the browser's displayed effective/base ATK without client calculations. The unit panel also shows remaining/max uses. To test legal-but-irrelevant Rage, disable relevance auto-choice and uncheck display filtering.

Run the browser renderer and interaction checks with `node --test ProjectDelve.Web.Tests/door-choice.test.cjs`.

The host routes each decision using its controlling `ActivationToken` and the state’s explicit `ControllerAssignment` list. The current adapters map `ControllerKind.Human` to submitted input and `ControllerKind.Automated` to the default provider; these serializable values contain no provider instances. The Engine and Console have no Web dependencies. Web tests cover the production catalog, fresh independent setups, restarts, activation/follow-up replacement, Shaman spawning and round bags, and the HTTP controls. Detailed courtyard interactions retain the old map as a test-only fixture. Run the full suite with `dotnet test ProjectDelve.sln`; renderer checks use the Node command above.


The shared Unit Card shows the active Unit, retains the most recently active Unit, and temporarily inspects a board Unit on hover. `UnitType.DisplayName` and domain-owned `CardEntryDescription` records supply names, categories and printed rules text for Actions, Free Actions, Bonus Actions, passives and playable follow-ups. Monster Behavior remains decision policy and is excluded from these entries. Concrete rules and usage counters retain their existing engine representations.

The API envelope also includes `presentation`, alongside the unchanged raw `result`. `presentation.cards` joins domain descriptions to current Unit counters. `presentation.decision` projects every authoritative candidate into `Direct`, `Unit`, `Position` or `Door` selection with an opaque key, supplied label, optional card-entry reference, relevance and separate `affectedUnitIds`. Optional decisions have a supplied `noneChoice` whose key is null. These interactions select complete candidates; the browser never constructs targets. Direct choices can appear on the matching card, with a panel fallback during hover inspection. The browser chooses control placement, abbreviations and generic previews without interpreting ability names or effects.

`presentation.events` supplies text and generic outcome roles; attack summaries are distinguished structurally from target results, independently of ability names. Events remain aligned one-to-one with raw engine events. `presentation.resolutionSteps` supplies card data at the same event indices as the existing authoritative `StateAfter` snapshots. Base/effective stats and progressive HP still come from those snapshots, not event reconstruction. No renderer-specific markup, colors, animation instructions or presentation cues are part of the domain/API contracts.

Barbarian's concrete `Fury` passive adds ATK +1 when at least two living hostile Units are adjacent. Side inequality determines hostility. Fury and Cleric Aura reuse the same eight-cell adjacency and normal LOS query as Goblin threat detection. `GameState.EffectiveAtkOf` derives Fury from the evaluated state's Units, positions, and board on every call, independently in copied/hypothetical states. Fury creates no stored modifier and stacks additively with Rage: base ATK 4 becomes 5 with Fury, 6 with Rage, or 7 with both. Aura retains its concrete adjacent-friendly DEF bonus representation and rules.
