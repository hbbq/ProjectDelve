# Content Browser

**Loot Management** lists effective Equipment and Items and the Loot Cards referencing
them by typed identity. Switch to **Loot Cards** to inspect rewards, static preset
quantities and dynamic All membership. Search and filter by family, origin and missing
cards. Select individual rewards or all visible missing rewards, preview, then confirm
one batch. IDs and names default to the reward's ID and name; collisions block the
whole batch. Static presets are never changed.

The default destination is Workspace. From an editable Scenario Designer, choose
Scenario-local to create the complete batch in one undoable operation. Save/Open,
recovery and DELVE1 use ordinary scenario content persistence.

**Workspace Management** lists Workspace-owned identities across supported families,
including Canonical overrides and dependency links. Select a subset, preview additions,
replacements and dependency decisions, then confirm publication. Required dependencies
available only in Workspace must be selected explicitly. An unselected Workspace
override does not prevent reuse of an existing Canonical dependency. Publication
validates the complete resulting Canonical catalog and checks preview state again.

Open **Content** from the Web header or **Explore game content** from Home.
The browser exposes the typed definitions used by Engine. Canonical content is
read-only; [Content Designer](CONTENT_DESIGNER.md) can create or edit Equipment, Items and
Loot Cards and complete Unit Types in the application-local Workspace or explicitly attached scenario draft. Search by
display name or Type ID, filter by category or origin, and select a definition.
Reference links open the related definition; **Previous definition** returns
to the previous selection and filters. At narrow widths, **All results** returns
from the detail panel to the list.

## Catalog and inspection

The canonical catalog includes Units, Equipment, Items, Loot Cards and presets,
World Cards and deck composition, Terrain, Fixed Components, Terrain assemblies,
Mechanics, Resources, Machines and Cell Markers. Counts come from Engine catalogs,
not a separate client inventory.

Unit inspection groups base stats, movement and structure, Loot configuration,
automated policies and owned Abilities. Each Ability shows its local key, owner,
name, timing, usage, generated description and declared mechanic parameters.
Equipment and Items expose their definitions and Abilities through the same
projection. Loot rewards link to their Equipment or Item definitions.

Terrain inspection distinguishes atomic and composed definitions. Assemblies link
to their surface, Fixed Components and resolved Terrain. Presentation recipes,
movement and LOS properties, categories and production parameters remain visible.
Production and summoning references lead to Resources and Unit Types respectively.
Advanced JSON is available for exact inspection; it is not an editing interface.

Mechanic pages use `MechanicCatalog` metadata. **Authored parameters** shows
requiredness, defaults, numeric/enum/list constraints and nested fields.
Supported source/timing/usage profiles are shown as combinations. **Fixed Engine
behavior** presents targeting, Unit context and system dependencies separately.
For example, `adjacentUnitsStatModifiers` exposes its relationship selector and
modifier list. Upright source, adjacency and self-exclusion rules remain fixed in Engine.

## Workspace and scenario contexts

Select **Workspace** for independent authoring. Edit in Workspace preserves a canonical
Type ID; Create Copy creates an independent identity. Saves validate the full typed
definition and use atomic writes with a conflict token. There are no revisions or
pins. Canonical promotion previews explicit additions, replacements and dependencies.
See CONTENT_DESIGNER.md for the workflow and unsaved-change protection.

In Designer, choose **Inspect content** to attach the current draft. The focused
draft-content view validates Equipment, Items and Loot dependencies without requiring
a complete board. The context selector also offers canonical content and **Full
scenario inspection**, which uses the existing complete-scenario resolution pipeline.
Returning to Designer preserves the workspace. Applied content edits participate in
Designer history and recovery; reopening content after draft changes refreshes its
context. Saves from stale editor sessions are rejected.

References resolve by Content Family and Type ID against **Scenario > Workspace > Canonical**.
New games capture their effective definitions. DELVE1 exports embed Workspace content
and its effective dependency closure as scenario-local definitions.
See [Scenario scopes](SCENARIO_CONTENT_REFERENCES.md).

Scenario results identify canonical, scenario-local, scenario override and resolved
entries. Units are limited to the scenario's declared types plus canonical types
reachable through summoning. A referenced type is not evidence of a placed Unit.
Other families follow their existing scoped catalog rules. Effective definitions
include Unit Loot overrides, composed Terrain, resolved Loot aliases and deck
quantities; authored declarations are retained in advanced inspection where useful.
Inline Resource and Machine variants with the same ID remain distinct entries,
and Resource links select the actual referenced definition.

Inspection never reads the live game's board, inventories, positions, counters,
choices or Fog of War observations. The scenario endpoint accepts explicit authored
input, creates a detached initial state for resolution, and returns definitions
only. It neither replaces nor advances the shared game. This is the same trusted
authoring boundary as Designer, not a way to inspect hidden running-game state.

## API and implementation

- `GET /api/content/catalog` returns the canonical catalog.
- `POST /api/content/inspect` accepts a full scenario JSON document and returns
  its scoped effective catalog. Invalid input returns HTTP 400 with diagnostic
  message, code and available field path, without substituting canonical content.
- Definition-only context, validation and preview endpoints are documented in
  [Content Designer](CONTENT_DESIGNER.md).

`ContentBrowserCatalog` projects Engine catalogs into entries, structured fields,
owned Ability cards and references. Mechanic schemas and reference annotations
come from the existing contracts; descriptions come from Engine content description
generation. The client renders those values and handles navigation and filtering.
No client rule dispatch or duplicate mechanic parameter table is involved.

Adding content to an existing Engine catalog makes it discoverable automatically.
A new content family needs a projection/category and applicable reference mapping.
New mechanics should follow [Mechanic Catalog registration](MECHANIC_CATALOG.md);
the viewer consumes their descriptor, including nested parameter metadata.

## Milestone limits

- Canonical and full-scenario inspection are read-only. Unit, Equipment, Item and Loot editing
  uses an explicit scenario or Workspace destination; other families remain read-only.
  There is no inheritance or runtime mutation.
- Full scenario inspection requires a valid complete scenario, including board
  and setup validation. Focused content editing only requires valid dependencies.
- Resources, Machines and Cell Markers expose their existing typed definitions;
  this milestone does not introduce new catalogs or effect models for them.
- Fixed behavior is described by Engine metadata and generated rules text, not
  executable expressions or an exhaustive description of handler internals.
- Catalog selection and working copies are kept in page memory; reload starts in
  canonical inspection. Saved library packages remain available through Library;
  scenario inspection uses the explicitly attached Designer snapshot.

Focused API tests cover catalog completeness, actual Ability parameters, scoped
resolution, links, duplicate inline identities, diagnostics and isolation from the
live game. Client tests cover search/filter counts, navigation, safe rendering,
context switching, stale requests and rejected drafts. Browser QA follows
[Browser inspection](BROWSER_INSPECTION.md).

Milestone 1 verification: the solution built with no warnings; all 2,542 .NET
tests and 815 Node tests passed. Focused inspection tests were rerun after the final
projection refinements. Edge inspection covered desktop and 360/390px layouts,
canonical Heroes, Equipment, Loot, World effects, Coal Lava, Campfire composition,
mechanic schemas, scoped Designer Abilities, reference history, collapsed groups
and empty searches. No browser errors or failed requests occurred in the completed
inspection plans.

## Promotion

Saved Library Unit/Equipment/Item/Loot and local or pinned Scenario definitions offer
**Promote to Canonical**. Select roots, preview the Engine dependency closure and
canonical reuse/conflicts, then explicitly confirm publication. The canonical
catalog refreshes after successful publication; original pins remain unchanged.
See [CONTENT_PROMOTION.md](CONTENT_PROMOTION.md) for APIs and ownership safeguards.
