# Content Workspace and Designer

Open Content to browse Canonical, Workspace or the current Scenario-local catalog. There is one application-local Workspace, independent of scenarios. See [storage](CONTENT_LIBRARY.md), [scope composition](CONTENT_COMPOSITION.md), [portable scenarios](SCENARIO_CONTENT_REFERENCES.md) and [publication](CONTENT_PROMOTION.md).

## Authoring

- Canonical **Edit in Workspace** opens the existing complete definition, preserving its Type ID.
- **Create Copy** proposes a new Type ID and preserves typed mechanic parameters.
- Workspace **Edit definition** saves the complete definition with a conflict token; **Delete Workspace definition** removes just that identity. **Empty Workspace** requires explicit confirmation.
- In Designer, **Inspect content** attaches the draft to Scenario-local browsing and its normal save, undo/redo and recovery pipeline. **Open Workspace** browses application content.
- **Promote to Canonical** previews selected additions/replacements and dependencies. Missing Workspace dependencies require explicit inclusion and a new preview; confirmation publishes exactly that batch and removes it from Workspace.

Unit, Equipment, Item and Loot Card editors reuse the existing destination-neutral forms, Unit authoring contract, Mechanic Catalog, Costs and Engine-generated text. No new family editors are introduced. Type IDs identify definitions; Ability local keys, mechanic identity, physical Instance IDs and display names remain distinct.

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

`POST /api/content/validate` accepts family, typed definition, optional existingId and dependency context. `POST /api/content/context` resolves a draft's typed definitions without requiring a valid board. Both resolve against current Workspace and Canonical. Structure, mechanic parameter bounds, source/timing/usage combinations and typed dependencies are checked by shared Engine validation. Diagnostics retain definition/field paths.

`GET /api/content/catalog` browses Canonical. `GET /api/content-workspace/catalog` shows effective Workspace content and origins, including active overrides. `POST /api/content/inspect` inspects a full scenario using the same effective content as new game creation. These authoring endpoints do not read running-game observations.

Generated Ability text and references appear beside recursive typed fields. Mechanic details retain parameter schemas, supported source/timing/usage combinations, targeting and description contracts. Advanced typed JSON remains available for inspection. Dirty forms retain their navigation/discard guard; failed saves retain authored content.

## Scenarios and games

Workspace edits affect newly created games. Existing games, replay and resumed snapshots retain captured definitions. Scenario-local definitions always win. Save/Open retains ordinary scenario authoring; DELVE1 exports embed effective Workspace definitions and their dependency closure, or report unsupported embedding. Export does not introduce package revisions or historical reference snapshots.

Historical Library services and tests remain internal legacy infrastructure; revision browsing, pinning and Update Reference are absent from the Content Browser workflow. Existing canonical rosters and authored campaigns are preserved. Future Campaign/runtime mutation scopes and new family editors are outside this milestone.
