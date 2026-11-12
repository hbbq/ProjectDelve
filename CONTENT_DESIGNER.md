# Content Workspace and Designer

Open Content to browse Canonical, Workspace or the current Scenario-local catalog. There is one application-local Workspace, independent of scenarios. See [storage](CONTENT_LIBRARY.md), [scope composition](CONTENT_COMPOSITION.md), [portable scenarios](SCENARIO_CONTENT_REFERENCES.md) and [publication](CONTENT_PROMOTION.md).

## Authoring

- Canonical **Edit in Workspace** opens the existing complete definition, preserving its Type ID.
- **Create Copy** proposes a new Type ID and preserves typed mechanic parameters.
- Workspace **Edit definition** saves the complete definition with a conflict token; **Delete Workspace definition** removes just that identity. **Empty Workspace** requires explicit confirmation.
- In Designer, **Inspect content** attaches the draft to Scenario-local browsing and its normal save, undo/redo and recovery pipeline. **Open Workspace** browses application content.
- **Promote to Canonical** previews selected additions/replacements and dependencies. Missing Workspace dependencies require explicit inclusion and a new preview; confirmation publishes exactly that batch and removes it from Workspace.

Unit, Equipment, Item, Loot Card, atomic Terrain and Terrain Assembly editors use destination-neutral forms and shared Engine validation. Ability editors consume the Unit authoring contract, Mechanic Catalog, Costs and Engine-generated text. Type IDs identify definitions; Ability local keys, mechanic identity, physical Instance IDs and display names remain distinct.

Terrain forms expose independent movement-permission and semantic-category lists, plus optional destruction-result references; no result selection means indestructible. An empty permission list blocks ordinary movement. The displayed Passable value is an Engine-derived land-permission projection, never an editable field. Assemblies explicitly select surface, components and destruction result. Copying preserves gameplay metadata independently of appearance. References participate in local validation, dependency closure and publication, including Machine Required Terrain Type references.

Area Break checks custom replacements in deterministic target order before rolling. Every combination of accepted successes and failures must remain legal. Unsafe occupied, Machine, Resource or Cell Marker replacements receive no roll or failure outcome. Walls are legal between all Terrain Types, so neighboring dry Cells becoming Water across a Wall can both be attempted when otherwise safe. This preflight also applies to guaranteed threshold-6 effects.

To author demolition content, add Break Object with threshold 1–6 as an Action or Bonus Action on a Unit or Equipment. Unlimited Equipment grants use the ordinary Unlimited usage selection. Threshold 6 means guaranteed success without a dice prompt. On a native Unit, add Explosion and Break Adjacent Objects as separate WhenDefeated Abilities, then use Move up/down to establish their order. For a timed explosive also add unlimited Defeat Self with InsteadOfActivation timing, and configure the summoning Ability's Initial Posture as Lying. The first activation stands it up; the next Upright activation defeats it. These are authoring patterns, not new canonical Types.

Unit definitions expose optional **Default Controller**, intrinsic **Amphibious** movement/occupancy, and the reusable ExploreFoW, Go To Friendly and Maximize Friendly Adjacent preferences through the same metadata-driven forms. Explicit scenario Group and Type/Side assignments override Type defaults. Existing Types with no default keep their existing assignment requirements. Amphibious does not grant Phase; Engine validation owns Water placement and movement legality. Multiple separately keyed Adjacent Unit Summon Abilities can coexist on one source, each retaining its own target Type, starting Posture and Side cap.

Units expose **Base Movement Capabilities**, defaulting to `land`; use `water` alone for a water-only Unit. The **Movement Capability Grant** mechanic adds one or more kinds through unlimited Passive Unit or Equipment Abilities. Both it and Amphibious remain effective while Lying. Lists accept additional normalized movement kinds without Engine branches. Terrain categories do not grant movement. Validate & preview and inspection use the same typed definitions as scenario-local JSON, Workspace and canonical C# content. Current runtime snapshots require terrain content version 3; unsupported development snapshots are rejected instead of silently guessing permissions.

### Ability Costs

Each Unit, Equipment and Single Use Item Ability has an ordered **Costs** section.
Choose a Cost and **Add Cost**, select its type, edit the typed fields, or use
**Remove Cost**, **Move up** and **Move down**. Lay Down Self has no parameters;
Spend Resource selects a Resource Type and a positive amount. Repeated Resource
payments retain their order and are checked cumulatively when invoked. Engine
validation rejects repeated Lay Down Self and reports indexed Cost field paths.

Available types and context combinations come from `CostCatalog` and the selected
mechanic's `CostProfiles`. Unsupported timing, source and usage combinations are
unavailable in the form. Imported incompatible Costs remain visible and removable;
the editor never silently discards them. **Validate & preview** shows Engine-generated
wording combining Costs and the mechanic, including supported wording overrides.
The browser does not generate gameplay descriptions or determine affordability.

Costs travel with the complete Ability through rename, reorder, Create Copy,
Workspace overrides and promotion, scenario undo/redo, recovery, Save/Open and
DELVE1. Physical Equipment counters and Item consumption continue using the same
Engine commitment and payment boundary.

## Validation and previews

Loot Management uses `POST /api/content/loot-management` with an optional typed
Designer `context` to inspect reverse reward and preset references. Batch generation
uses `/api/content-workspace/loot/preview` with `{expectedToken, selected,
destination, context}`; selected identities use Engine Content Family names.
`/api/content-workspace/loot/create` accepts `{preview, confirm:true}` and rebuilds
the proposal under Workspace and Canonical locks. Invalid or stale batches create
no definitions. Scenario destination returns validated typed cards to the Designer's
single history operation without persisting them to Workspace.

`POST /api/content/validate` accepts family, typed definition, optional existingId and dependency context. `POST /api/content/context` resolves a draft's typed definitions without requiring a valid board. Both resolve against current Workspace and Canonical. Structure, mechanic parameter bounds, source/timing/usage combinations and typed dependencies are checked by shared Engine validation. Diagnostics retain definition/field paths.

`GET /api/content/catalog` browses Canonical. `GET /api/content-workspace/catalog` shows effective Workspace content and origins, including active overrides. `POST /api/content/inspect` inspects a full scenario using the same effective content as new game creation. These authoring endpoints do not read running-game observations.

Generated Ability text and references appear beside recursive typed fields. Mechanic details retain parameter schemas, supported source/timing/usage combinations, targeting and description contracts. Advanced typed JSON remains available for inspection. Dirty forms retain their navigation/discard guard; failed saves retain authored content.

## Scenarios and games

Workspace edits affect newly created games. Existing games, replay and resumed snapshots retain captured definitions. Scenario-local definitions always win. Save/Open retains ordinary scenario authoring; DELVE1 exports embed effective Workspace definitions and their dependency closure, or report unsupported embedding. Export does not introduce package revisions or historical reference snapshots.

Historical Library services and tests remain internal legacy infrastructure; revision browsing, pinning and Update Reference are absent from the Content Browser workflow. Existing canonical rosters and authored campaigns are preserved. Future Campaign/runtime content-editing scopes remain outside this milestone.
