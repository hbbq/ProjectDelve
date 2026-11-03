# Content Designer: Units, Equipment, Items and Loot Cards

Open **Explore game content** from Home and select **Library** to author Equipment
and Loot Cards, or complete Unit Types and Single Use Items, without a scenario. Canonical content remains read-only. In Scenario
Designer, **Inspect content** attaches the scenario draft instead. Both destinations
use the same forms, typed definitions, Mechanic Catalog and Engine-generated previews.
The destination is not part of a definition's Type ID or mechanic declaration.

Scenario Designer's **Use library content** attaches the current draft to Library
browsing. **Use in Scenario** previews a saved revision's selected definitions and
typed dependencies; Apply embeds a portable, read-only pin as one undoable change.
Later Library edits do not change the scenario. **Update Reference…** previews an
explicit revision change before applying it. See
[Pinned scenario references](SCENARIO_CONTENT_REFERENCES.md) for the workflow,
composition policy, API and portability guarantees.

Select **Promote to Canonical** on saved Library content or scenario-local/pinned
Units, Equipment, Items and Loot to preview dependencies and explicitly publish a new canonical
JSON package. See [Promotion](CONTENT_PROMOTION.md) for revision checks, ownership
and portability. Canonical definitions remain read-only.

## Standalone library workflow

1. Select **Library**, then **Create package**. Enter a stable package ID and title,
   and choose **Start package**. This starts a working copy, not a stored revision.
2. Choose **Create Equipment**. Enter its Type ID/name and configure its owned
   Abilities, modifiers, capabilities and usage through the shared form.
3. **Validate & preview** shows Engine diagnostics and generated wording.
   **Save definition** applies the definition to the package working copy.
4. Select that Equipment and choose **Create Loot Card for this Equipment**.
   Give the card its own stable ID/name, preview it, and save the definition.
5. **Save package** validates and commits one immutable revision containing both
   definitions. Reopening the package after a restart retrieves that revision.
6. Edit an owned definition and save the package again to create another revision.
   **Revision history** selects older, read-only versions; **Reload current revision**
   returns to the editable head. Package and origin library IDs remain visible.

Package metadata and applied definitions remain in a page-local working copy until
Save package. Cancel package changes offers discard or keep editing. Switching
packages, selecting history or leaving Content protects unsaved work; unfinished
forms have their own save/discard guard. Browser reload/close uses the native
unsaved-changes warning. This milestone does not add library-draft crash recovery.
Scenario draft recovery and undo/redo continue unchanged.

Save uses the captured expected revision. A stale edit leaves the working copy
intact and shows the conflict; reload the current revision explicitly after saving
your work elsewhere or agreeing to discard. There is no automatic overwrite/merge.

The Dependencies panel selects canonical providers and exact external library
revisions. New packages explicitly select the installed canonical providers by
default. Search external packages by name/ID and use a selected revision. Only
that selection and its typed transitive closure enter the reference catalog;
other installed library packages are never ambient dependencies. Engine reports
missing references, family conflicts and invalid parameters before saving.
Package validation failures keep dependency controls available for repair.

Reference pickers search the selected effective definitions by name or Type ID.
Linked Loot can reward Equipment in its own package, selected canonical content,
or a pinned external library revision, and authored Items. References and
generated previews navigate to the authoritative definitions and mechanic contracts.

**Copy as new** creates an independent Unit, Equipment, Item or Loot definition in the current
library package, proposing a fresh stable Type ID. Its existing dependency references
and mechanic parameters are retained. Inspect canonical entries or entries from
selected external revisions and copy them into the package; select the required
dependencies before preview/save. A copied definition has no live inheritance link.
Owned Type IDs become read-only after applying the definition. Historical revisions
and external definitions cannot be edited in place.

## Editing workflow

### Single Use Items

Choose **Create Single Use Item** in a Library package or attached scenario draft.
Enter a new stable Type ID and name, then add at least one Ability. The shared
Ability editor uses Item-specific Engine profiles for mechanics, timings and
parameter controls. Rename, remove and reorder declarations while retaining their
owner-local keys. Summon references include the effective scenario-local Unit Types.
**Validate & preview** shows field diagnostics and Engine-generated descriptions.

Item Ability usage is unlimited in the typed envelope: using any one Ability
consumes the selected physical Item. There are no persistent charges or Item
Passives. Multiple copies retain ordinary Action and per-Type Bonus Action allowances.
Saving a Type creates no physical object.

After saving, **Create Loot Card for this Item** opens a linked reward containing
the Item Type ID. Save both definitions and then the Library package. Reward
pickers also include canonical Items and explicitly selected Library dependencies.
Items support Copy as new, immutable revision history, Use in Scenario, pinned
references, Update Reference and explicit canonical JSON promotion. Scenario Items
appear in inventory, known ground objects, Loot and Playtest through existing setup
controls. Undo/redo, recovery, Save/Open and DELVE1 retain the same typed definitions.

### Unit Types

Choose **Create Unit** in Library or the attached scenario draft. Identity, base
stats, uniqueness, footprint, normal Attack availability, movement patterns and
Terrain restrictions use typed controls. Capabilities include Item/Equipment
ownership; Open Door, Phase and other established capabilities are local Abilities.
Loot uses the existing d6 success-face declaration. Resource Storage edits its
owned Resource ID/name/capacity payloads without turning them into global types.
Automation preferences and Attack definition order do not grant gameplay actions.

The shared Ability editor filters Mechanic Catalog profiles for Unit sources.
Add, remove, rename and move Abilities without changing their owner-local keys.
Parameter bounds, timing, usage and generated descriptions come from Engine.
Unsupported combinations, invalid stats, HP/Unique constraints, storage and
references produce definition diagnostics without requiring a playable board.

Saving a Unit makes it available in declaration, placement and summon selectors;
it does not place a figure or assign agency. Scenario-local definitions still
require explicit declaration before full scenario validation/Playtest. Board
validation checks existing placements against edited footprints and movement.
Self-summoning is supported; mutually summoning Units can be created as valid
definitions first and then edited to reference each other. Library revisions can
also carry mutually referencing definitions in one package transaction.

Units support Copy as new, portable read-only pins, Update Reference and explicit
canonical promotion through the same workflows as Equipment/Loot. Existing local
Unit overrides remain editable in their scenario; new editor identities cannot
claim provider IDs. Canonical/pinned definitions are not edited in place.

Create Equipment, or copy an existing definition into the draft under a new Type
ID. Edit its name and its owned Abilities, modifiers and capabilities. The mechanic
selector lists only operations that support Equipment. Timing, usage, parameters,
list entries and description overrides follow the Mechanic Catalog's contracts.
For a stat bonus, select Self Modifiers and configure its modifier list. Phase and
Open Door are also owned Ability declarations, with their established timing.
Supported active Equipment Abilities may have unlimited usage or a positive number
of uses per game. Counters belong to the physical Equipment instance and survive
defeat and pickup; continuous Passives remain unlimited.

Preview validates the definition and displays Engine-generated descriptions.
Save validates again and applies one undoable scenario change or a library working-copy change. Cancel
leaves the destination unchanged; leaving an unfinished edit offers discard or
continue editing. Type IDs stay fixed after creation. Ability keys are independent
of display names and remain stable through renaming and reordering.

After saving Equipment, create a Loot Card that references its Type ID. Loot can
instead reference an Item or use a supported Immediate mechanic. A reward
references its independent definition; it never copies that object's mechanics.
Search the effective catalog by name or ID to choose references.

For Summon Adjacent, select a Unit Type and initial Posture. The picker includes
canonical Units and scenario-local definitions, using the scenario's display names
and overrides. Preview and validation resolve the same references as the Engine;
the content form needs no Unit placement or running game. Other supported summoning,
Resource, racing and specialized mechanics appear through their catalog profiles.
Their normal storage, geometry, posture and timing requirements still apply.

Return to Designer and select **All** Loot to include one of each effective Loot
Card, including newly authored cards. Alternatively, select Custom Loot, set the
new card's quantity and apply the deck. Place unopened Loot markers within that deck's finite supply, or supply the
Equipment directly through individual inventory or known ground objects. **Test
scenario** uses the same definitions and Engine acquisition rules as other content.
Saving a content definition changes the draft; **Save to library** saves the scenario.

## Scope and persistence

| Scope | Current behavior | Future extension |
| --- | --- | --- |
| Canonical | C# and JSON providers, read-only; Create Copy uses a new ID | Canonical replacement remains separate |
| User Content Library | Standalone working copies, immutable revisions and explicit portable scenario pins | No canonical replacement editor |
| Scenario-local | Editable Units/Equipment/Items/Loot and explicit canonical promotion | Additional family editors |

The form consumes a definition and a dependency catalog. Validation consumes the
same data, without requiring a board, Unit placement or live game. The destination
adapter supplies a label, current dependency context, revision and save operation.
The scenario adapter writes `unitTypes`, `equipmentTypes`, `itemTypes` or `lootCardDefinitions` in
`DesignerDraft`. The library adapter writes the same fields in a package working
copy; its package save uses the revisioned Content Library service.

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

- `POST /api/content/context`: dependency arrays `unitTypes`, `equipmentTypes`,
  `itemTypes` and `lootCardDefinitions`; returns effective Unit and object-content
  catalogs, Mechanic contracts and Resource references. Local Unit definitions
  override canonical definitions only in that supplied context.
- `POST /api/content/validate`: `family` (`units`, `equipment`, `items` or `lootCards`), `definition`,
  optional `existingId`, and the dependency `context`. Returns `valid`, field
  diagnostics and, on success, the typed definition and authoritative preview entry.

Neither endpoint persists content or creates a game. `existingId` identifies an
existing destination entry, not a canonical override. New definitions must not
collide with canonical or destination IDs. Engine catalog validation,
`UnitContent.Resolve`, `AbilityContent.ValidateObjectCatalogs` and Mechanic Catalog contracts supply the
authoritative checks. Form constraints prevent unsupported choices where practical;
they do not replace Engine validation or determine gameplay legality.

Library destination endpoints use the same typed parser and Engine composition:

- `GET /api/content-library/providers` lists installed canonical provider IDs.
- `POST /api/content-library/authoring/context` accepts a library save request
  (`package`, `expectedRevision`, `bindings`) and returns its effective authoring
  catalog, with package/revision provenance and library-local/dependency origins.
- `POST /api/content-library/authoring/validate` accepts `{draft, edit}`, where draft
  is that save request and edit uses the existing content editor request shape.
  It returns the same validation/preview response used by the form. Only Unit,
  Equipment, Item and Loot candidates owned by the destination can be edited; IDs stay fixed.
- `GET /api/content-library/packages/{id}/context?revision=N` inspects a saved
  revision with its captured dependency context, including historical revisions.

Preview candidates use an isolated library document and never advance a head or
commit a candidate revision. Final save uses the existing package transaction,
expected-revision check, typed dependency graph and atomic persistence. See
[CONTENT_LIBRARY.md](CONTENT_LIBRARY.md) for storage, revision and transfer APIs.

Catalog responses include `unitAuthoring`, Engine metadata for existing Unit fields,
defaults, enum choices and numeric domains. It is not a separate authoring model.
The editor renders mechanic parameters recursively from their schemas. It contains
no per-mechanic forms, gameplay expressions or copied numeric constraints. Modifier
lists and capabilities remain in `localAbilities`; there is no parallel editor-only
Equipment model. Preview descriptions come from the existing Engine projections.

Scenario library references use dependency collection and explicit Type ID checks.
Promotion performs explicit Type ID conflict handling and validates the complete
proposed canonical catalog. These are scope
operations, not changes to the definition's identity. A canonical JSON provider can
load the same validated typed definitions alongside C# rosters; promotion need not
generate C#. Providers already exist; promotion uses the validated atomic JSON publication service.

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

Single Use Item milestone verification: the solution builds with zero warnings or
errors. All 2,189 Engine tests, 629 Web tests and 883 JavaScript/dispatcher tests
pass. The normal parallel Web run reproduced the documented Gambit HTTP SSE
timeout; the full Web rerun passed with test collections serialized using a
temporary runner configuration. No assertions or checked-in runner settings changed.

`ItemContentAuthoringTests` covers standalone Library previews, linked Loot,
scenario-local definitions, empty/invalid Abilities, Item usage contracts,
references, copies, immutable history, stale saves, pin/update closure, DELVE1,
scenario storage, promotion, multiple physical copies, choosing either Ability of
a two-Ability Item, Action budgets and deterministic save/resume/replay. Existing
Engine accounting/projection tests retain Bonus Action and FoW coverage. Client
tests cover Item source filtering, keys through rename/reorder/edit/copy,
linked Loot, scenario history/recovery, read-only pins and promotion selection.

Desktop and 390px Edge inspection created Last Resort Fireball and Goblin in a
Bottle, plus linked Loot Cards, entirely through Content Designer. Both were saved
in an isolated Library, pinned into a saved scenario, acquired from its two-card
Loot deck and used in Playtest. Fireball consumed its selected copy before its dice
continuation; the bottle consumed its copy and placed a Lying Goblin. Scenario-local
Item/Loot creation, Library revision history, canonical promotion and reopening
matching retained pins were also inspected. Checked-in canonical content and the
existing host were preserved.

The established promotion conflict policy still applies: pins with a different
payload for a newly canonical identity cannot compose with that publication.
Promote the revision matching retained pins or explicitly update those references
before publication. No automatic reference migration is introduced.

`UnitContentAuthoringTests` covers complete Unit declarations, stat and HP/Unique
diagnostics, malformed Abilities, movement and storage, Behavior flags, independent
copies, self/mutual summons, immutable Library history, pin/update/promotion,
embedded Resource variants, placement validity, DELVE1, Playtest, save/resume and
FoW observations. Client tests cover metadata-driven Unit fields, stable Ability
rename/reorder, scenario destinations, new declaration/placement choices, revision
guards, undo/redo and recovery. Desktop/mobile Edge inspection exercises Library
creation/reopening, scenario pins and placement, local Unit editing, DELVE1 and
recovery, Playtest, and canonical publication with generic visual fallbacks.

`ContentLibraryAuthoringTests` covers non-persisting previews, standalone linked
Equipment/Loot, immutable history, explicit external references, canonical copies,
diagnostic paths and stale revisions. Library persistence/restart and game/scenario
isolation use the existing Content Library suites. Client destination tests cover
working-copy stamps, independent copy IDs/parameters, selected dependency pickers,
failed validation, conflicts, history and unsaved-navigation guards.

Milestone 5 verification passed all 2,756 .NET tests and 854 JavaScript tests.
Edge exercised package creation from Home, Equipment with limited uses, linked
Loot, save/reload, revision editing and historical inspection at desktop and
390px widths. A restarted host reopened old/current definitions; separate checks
covered external revision selection, Create Copy and Keep/Discard navigation.

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

Editing is limited to Unit, Equipment, Item and Loot Card definitions. Other families remain
inspectable. There is no content
deletion, inheritance, live variant linkage, global mutable catalog, runtime editing,
asset upload or new gameplay mechanic. Applied definitions are recovered with the
Designer draft; library working copies remain in page memory until Save package.
Unfinished content forms must be saved or discarded before leaving. Additional family editors remain deferred. Library pins are read-only; Create Copy
is the explicit route to independent scenario-local content.

See [Equipment content](EQUIPMENT_CONTENT.md), [Loot content](LOOT_CONTENT.md),
[Ability content](ABILITY_CONTENT.md) and [Mechanic Catalog](MECHANIC_CATALOG.md)
for the authoritative model and supported combinations.
