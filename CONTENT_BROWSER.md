# Content Browser

Open **Content** from the Web header or **Explore game content** from Home.
The read-only browser exposes the typed definitions used by Engine. Search by
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
For example, `adjacentModifiers` exposes its modifier list, while its friendly
recipient rule is fixed. The browser does not make that rule configurable.

## Scenario context

In Designer, choose **Inspect content** to inspect a snapshot of the current draft.
This validates and resolves the snapshot using the existing scenario pipeline.
The context selector switches between canonical content and that inspected draft.
Editing the draft afterward requires another **Inspect content** to refresh it.
Returning to Designer preserves the workspace.

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

- Inspection is read-only. There is no content editing, variant creation,
  inheritance, runtime mutation or canonical JSON catalog migration.
- Scenario inspection currently requires a valid complete scenario, including
  board and setup validation. Incomplete drafts report validation diagnostics.
- Resources, Machines and Cell Markers expose their existing typed definitions;
  this milestone does not introduce new catalogs or effect models for them.
- Fixed behavior is described by Engine metadata and generated rules text, not
  executable expressions or an exhaustive description of handler internals.
- Catalog selection and the last inspected draft are kept in page memory; reloading
  the page returns to canonical inspection until Designer supplies another snapshot.

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
