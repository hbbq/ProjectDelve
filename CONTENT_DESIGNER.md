# Content Designer: Equipment and Loot Cards

The Content Browser remains available from Home without opening a scenario.
Canonical content is read-only. Open a scenario in Designer and choose
**Inspect content** to attach that draft as the destination for new or edited
Equipment and Loot Cards. Definitions are ordinary typed content; the destination
is not part of their Type ID or mechanic declaration.

## Editing workflow

Create Equipment, or copy an existing definition into the draft under a new Type
ID. Edit its name and its owned Abilities, modifiers and capabilities. The mechanic
selector lists only operations that support Equipment. Timing, usage, parameters,
list entries and description overrides follow the Mechanic Catalog's contracts.
For a stat bonus, select Self Modifiers and configure its modifier list. Phase and
Open Door are also owned Ability declarations, with their established timing.
Equipment has no per-game Ability charges.

Preview validates the definition and displays Engine-generated descriptions.
Save validates again and applies one undoable change to the destination. Cancel
leaves the destination unchanged; leaving an unfinished edit offers discard or
continue editing. Type IDs stay fixed after creation. Ability keys are independent
of display names and remain stable through renaming and reordering.

After saving Equipment, create a Loot Card that references its Type ID. Loot can
instead reference an existing Item or use a supported Immediate mechanic. A reward
references its independent definition; it never copies that object's mechanics.
Search the effective catalog by name or ID to choose references.

Return to Designer and select **All** Loot to include one of each effective Loot
Card, including newly authored cards. Alternatively, select Custom Loot, set the
new card's quantity and apply the deck. Place unopened Loot markers within that deck's finite supply, or supply the
Equipment directly through individual inventory or known ground objects. **Test
scenario** uses the same definitions and Engine acquisition rules as other content.
Saving a content definition changes the draft; **Save to library** saves the scenario.

## Scope and persistence

| Scope | Milestone 1 behavior | Future extension |
| --- | --- | --- |
| Canonical | Globally available, read-only; Create Copy uses a new ID | Validated promotion and JSON catalog providers |
| User Content Library | Not implemented | Reusable definitions and dependencies independent of scenarios |
| Scenario-local | Editable Equipment and Loot stored in the explicit draft | Copy to another scope or promote with dependency/conflict resolution |

The form consumes a definition and a dependency catalog. Validation consumes the
same data, without requiring a board, Unit placement or live game. The destination
adapter supplies a label, current dependency context, revision, snapshot and save
operation. The initial adapter writes `equipmentTypes` or `lootCardDefinitions` in
`DesignerDraft`; a future library adapter can persist the same payload elsewhere.

Content changes participate in Designer history, browser recovery, draft files,
library Save/Open, JSON/DELVE1 transport and Playtest. Unrelated fields remain intact.
A changed draft or different open document invalidates an outstanding content save;
reopen the definition from the current destination instead of overwriting newer work.

The focused draft-content view works even before a board/setup is complete. The
full scenario inspection option retains the existing all-family resolved view and
its complete-scenario validation. Canonical browsing remains independent of either.
These authoring contexts do not read hidden running-game state.

Older scenarios may contain a local Loot Card whose ID now collides with canonical
content. The focused catalog shows that historical definition separately and keeps
it read-only; Create Copy gives it an independent ID. Existing deck compatibility
remains intact. New definitions cannot introduce such collisions.

## Validation and preview APIs

- `POST /api/content/context`: dependency arrays `equipmentTypes`, `itemTypes` and
  `lootCardDefinitions`; returns the effective object-content and Mechanic catalogs.
- `POST /api/content/validate`: `family` (`equipment` or `lootCards`), `definition`,
  optional `existingId`, and the dependency `context`. Returns `valid`, field
  diagnostics and, on success, the typed definition and authoritative preview entry.

Neither endpoint persists content or creates a game. `existingId` identifies an
existing destination entry, not a canonical override. New definitions must not
collide with canonical or destination IDs. Engine catalog validation,
`AbilityContent.ValidateObjectCatalogs` and Mechanic Catalog contracts supply the
authoritative checks. Form constraints prevent unsupported choices where practical;
they do not replace Engine validation or determine gameplay legality.

The editor renders mechanic parameters recursively from their schemas. It contains
no per-mechanic forms, gameplay expressions or copied numeric constraints. Modifier
lists and capabilities remain in `localAbilities`; there is no parallel editor-only
Equipment model. Preview descriptions come from the existing Engine projections.

Future library references and promotion need dependency collection, explicit Type
ID conflict handling and validation against the target catalog. These are scope
operations, not changes to the definition's identity. A canonical JSON provider can
load the same validated typed definitions alongside C# rosters; promotion need not
generate C#. This milestone adds neither the provider nor promotion commands.

## Presentation and future assets

Gameplay definitions and asset references remain separate. New Equipment and Loot
use existing textual/card and rendering fallbacks; no new Type ID is added to an
artwork dispatch table. `loot-ui.js` continues to render authoritative names and
descriptions with a generic illustration for unfamiliar objects. Existing Classic,
Enhanced and 3D rendering behavior is preserved.

Future asset work can attach presentation metadata referring to independent asset
IDs, with an adapter for existing artwork/model catalogs. It should cover image and
icon upload, 3D model upload, selection/reuse, and Classic/3D previews without adding
mechanic parameters. Asset validation belongs at that boundary: allowed formats,
image dimensions, model/material constraints, provenance and packaging dependencies.
Copying or promoting content should resolve referenced assets and their availability
along with content dependencies. No asset manager, upload route or new presentation
field is introduced here.

## Verification

`ContentEditorApiTests` covers definition validation, schema diagnostics, supported
contexts, references, copy conflicts and independence from board validity.
`ContentEditorWorkflowTests` creates Equipment and its Loot reward through validation,
round-trips DELVE1 and library storage, acquires the physical Equipment in Playtest,
uses its granted Action and compares uninterrupted execution with save/resume.

The client tests cover form schemas, previews, references, stale requests, copying,
discard protection, destination history/recovery and Playtest snapshots. The Cell
inspector also preserves unopened Loot marker counts when a numeric field loses
focus, so the edited reward can be placed and played through the ordinary Designer.

For browser verification, use an isolated scenario library and the procedure in
[Browser inspection](BROWSER_INSPECTION.md). Create Equipment with multiple modifiers,
an Action and a capability; create its Loot Card; save/reopen the scenario; add that
card to Custom Loot and place a marker; then acquire it in Playtest. Check generated
descriptions and effective stats, and repeat editing/validation at a narrow width.

## Limits

Editing is limited to Equipment and Loot Card definitions. Other families remain
inspectable, and existing Item Types can be selected as rewards. There is no content
deletion, inheritance, live variant linkage, global mutable catalog, runtime editing,
asset upload or new gameplay mechanic. Applied definitions are recovered with the
Designer draft; unfinished content forms must be saved or discarded before leaving.

See [Equipment content](EQUIPMENT_CONTENT.md), [Loot content](LOOT_CONTENT.md),
[Ability content](ABILITY_CONTENT.md) and [Mechanic Catalog](MECHANIC_CATALOG.md)
for the authoritative model and supported combinations.
