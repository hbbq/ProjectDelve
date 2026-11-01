# Content Library and Canonical Content Architecture

Investigation and design proposal, 2026-10-31. This document records findings and
recommendations, not implemented functionality or approved changes to game rules.
[CONSTRAINTS.md](CONSTRAINTS.md) remains authoritative. The investigation was
read-only; no test suites were run.

The investigation reviewed [AGENTS.md](AGENTS.md),
[CONSTRAINTS.md](CONSTRAINTS.md), [CONTENT_BROWSER.md](CONTENT_BROWSER.md),
[CONTENT_DESIGNER.md](CONTENT_DESIGNER.md), [ABILITY_CONTENT.md](ABILITY_CONTENT.md),
[MECHANIC_CATALOG.md](MECHANIC_CATALOG.md), and the relevant Engine, Web, Designer,
serialization, catalog, persistence and generator code.

The recommendation is **one shared content composition boundary**, with C#
rosters, canonical JSON, libraries and scenario-local definitions supplying the
same typed models. Engine execution should receive an explicit, validated content
snapshot; it should not discover additional content through canonical fallbacks
during play.

The existing editor and Ability models provide a good foundation. The main work
is catalog resolution, dependency capture and persistence.

**The current architecture already separates mechanics from many content declarations, but catalog ownership remains implicit.**

| Area | Current architecture and coupling |
| --- | --- |
| Units | `CanonicalUnitTypes` uses C# roster factories. Scenario-local Units can override canonical IDs. `UnitContent.Find` falls back to canonical content even when supplied an explicit catalog. |
| Equipment and Items | Typed definitions support C# and scenario JSON. Their resolvers prepend canonical definitions and reject duplicate IDs. |
| Loot | Typed rewards reference Equipment or Items. Authoring resolution, runtime resolution and historical collision handling differ. Presets introduce additional central dependencies. |
| World Cards | Typed definitions, but catalog resolution and default deck composition depend on static canonical content. |
| Terrain and Fixed Components | Typed atomic definitions and assemblies already exist. Assembly references are explicit, but defaults and validators consult canonical rosters. |
| Resources and Machines | Primarily embedded definitions rather than independently resolved catalogs. Different inline payloads can share a Type ID. |
| Cell Markers | Banana Peel has a concrete implementation; this is not yet an open, independently authorable family. |
| Mechanics | Explicit Engine registrations and schemas. These can remain available without any installed game content. |
| Designer | Definition-only validation and a destination adapter already exist. Editable families are currently Equipment and Loot Cards. |
| Persistence | `ScenarioLibrary` persists scenarios and campaigns with revisions, conflict detection and atomic writes. It does not store independent content definitions. |

These distinctions are visible in
[UnitContent.cs](ProjectDelve.Engine/UnitContent.cs),
[ScenarioCreation.cs](ProjectDelve.Engine/ScenarioCreation.cs),
[ContentEditorApi.cs](ProjectDelve.Web/ContentEditorApi.cs) and
[ScenarioLibrary.cs](ProjectDelve.Web/ScenarioLibrary.cs).

There is also a snapshot gap: a summoned Unit Type absent from `GameState.Types`
can be fetched from current canonical content when it is first placed. Saving
before that summon therefore does not necessarily preserve its definition.
Capturing only placed or initially declared Units is insufficient. See
[Model.cs](ProjectDelve.Engine/Model.cs).

**Scope should belong to the containing document or package, never the gameplay definition.**

Introduce three conceptual components:

- A **typed content bundle** containing existing family definitions, presets and compositions.
- A **resolver** that composes explicitly selected bundles, validates references and produces effective catalogs.
- **Provenance metadata** recording package, scope, revision, authored location and editability alongside those catalogs.

C# rosters and JSON files become providers of the same bundle model. Storage and
HTTP remain outside Engine rules. This does not require a new assembly structure
immediately.

| Scope | Ownership and behavior |
| --- | --- |
| Canonical | Standard installed content. Initially supplied by both C# and JSON providers, with exactly one authoritative declaration per identity. |
| Library | Independently authored, persistent packages. Editing creates a new revision. No scenario is required. |
| Scenario-local | Definitions embedded in one scenario, retaining current isolation and supported override behavior. |

For initial resolution, retain `(content family, Type ID)` as the Engine lookup
identity. Package identity and revision identify where a definition comes from;
they do not replace Type IDs, Ability keys or physical Instance IDs.

Use these collision rules:

1. Import only explicitly selected library content and its dependencies, not every installed library definition.
2. Reject unrelated definitions with the same family and Type ID. Package load order must never choose a winner.
3. Reuse an already selected identical revision of the same identity.
4. Reject incompatible revisions of one identity within a single effective catalog. Offer an explicit update or Create Copy.
5. Preserve existing scenario-local Unit overrides, recording which base they override. Do not generalize that behavior to Equipment, Items or other families.
6. Keep historical Loot alias conversion at an import boundary; it must not become the package collision policy.

Terrain assemblies and atomic Terrain already share a namespace and should retain
that constraint. Inline Resource and Machine variants need special care: their
current same-ID payloads must remain distinct owned declarations until a deliberate
reusable-reference model is introduced. Browser-generated `:variant:N` keys are
not persistent identities. See
[ContentBrowserCatalog.cs](ProjectDelve.Web/ContentBrowserCatalog.cs).

**Library references should be pinned and embedded; copying and inheritance should remain separate operations.**

| Operation | Recommended semantics |
| --- | --- |
| Create Copy | New Type ID and independent declaration. Existing dependencies remain references unless the user explicitly copies them too. |
| Reference Library Content | Preserve identity; select an exact immutable revision. Embed the selected library dependency closure in the scenario for portability. |
| Update References | Explicitly show changes, validate the resulting graph and apply one undoable scenario change. |
| Create Variant, later | New identity plus a base reference and typed overrides. Materialize a concrete definition before execution. No inheritance in the first milestone. |
| Promote | Preserve intended content identity while publishing it into canonical ownership. This is not Create Copy. |

For copying a dependency group, generate an explicit old-to-new identity mapping
and rewrite known typed references. Do not rewrite arbitrary strings or change
owner-local Ability keys unnecessarily.

Future variants can inherit base changes when their authoring revision is rebuilt
or published. Scenarios using a published variant should still remain pinned.
Collection overrides should use stable keys, such as Ability keys, rather than
fragile array positions.

There is one important existing-policy exception: **canonical Loot presets are
explicitly live references resolved at new-game creation**. Generator recipes also
retain those references. Silently freezing them when saving a scenario would
change established behavior. See
[Live Loot configuration](CONSTRAINTS.md#live-loot-configuration).

The recommendation is:

- Pin library definitions and their library dependencies when attached to a scenario.
- Preserve existing central canonical-reference and live-preset behavior initially.
- At game creation, resolve the complete effective content and capture it, including canonical dependencies, future summon targets and actual deck composition.
- Resume that game from its captured content, without consulting newer library or canonical definitions.

This prevents library edits from changing existing scenarios while preserving
current canonical policy. A fully frozen scenario mode, including canonical
presets and card definitions, needs an explicit policy decision.

**Canonical promotion should be a validated publication operation with a concrete preview.**

The operation should:

1. Capture the selected definitions and dependencies from one consistent source revision.
2. Compute the dependency closure.
3. Identify dependencies already satisfied by canonical content.
4. Include library or scenario-local dependencies that must be promoted together.
5. Report missing references, identity conflicts and proposed changes.
6. Validate the complete proposed canonical catalog.
7. Publish the target package atomically, conditional on unchanged source and destination revisions.

Canonical packages should not depend on mutable entries in a user's local library.

Preserve Type IDs when promoting. If the ID belongs to unrelated canonical content,
stop with a conflict; copying under a new identity is a separate operation.
Replacing an existing canonical definition should require an explicit update
operation.

If C# already supplies the same canonical identity, promotion cannot simply add a
competing JSON declaration. That entry must be deliberately migrated to the JSON
provider, removing its C# registration.

After successful publication, archive or redirect the original editable entry
only if its revision is unchanged. Retain historical revisions for existing
references. Publication and source cleanup should be safely retryable.

The existing [LibraryPromotion.cs](ProjectDelve.Web/LibraryPromotion.cs) offers a
useful preview/apply pattern, but currently promotes campaigns and scenarios. Its
separate source reads also demonstrate why a new content promotion must capture
the whole dependency graph consistently.

Initially, Designer can prepare a promotion preview and export a validated bundle.
A development command can publish it into the configured canonical directory. A
directly writable canonical destination can be added without changing the
underlying operation.

**Simple versioned JSON packages are sufficient for initial storage.**

Suggested locations, not existing contracts, are:

| Purpose | Suggested storage |
| --- | --- |
| Checked-in canonical JSON | `Content/Canonical/<package-id>/package.json` |
| User library revisions | `App_Data/ContentLibrary/revisions/<hash>.json` |
| Current library package revisions | `App_Data/ContentLibrary/library.json` |
| Scenario imports | Reference metadata and embedded library snapshots inside the scenario document |
| Sharing | Exported JSON package containing selected definitions and required dependency revisions |

A package document should contain:

- Format version.
- Stable package ID and revision.
- Optional title, description, author and license metadata.
- Typed definition collections.
- Explicit dependency bindings.
- Content digest.

Use immutable revision files and a single atomic manifest replacement. Reuse the
current library's locking, expected-revision checks and write/replace practices.
Validate saves again against the server's captured dependency revision.

Serialization must be deterministic without changing meaningful order. Preserve
Ability order, deck composition, Unit declarations and established canonical
enumeration order where consumers depend on it. Stable serialization does not mean
alphabetically sorting every array.

Do not serialize generated descriptions as independent authority. For Terrain,
store authored assembly declarations and derive their resolved form; avoid
maintaining both as competing source definitions.

Import/export needs typed validation, bounded input, conflict reporting and
explicit identity mappings. It does not need dependency ranges, a remote registry,
automatic upgrades or executable package hooks.

**Dependency validation should become reusable Engine functionality.**

Separate three layers:

1. **Definition validation:** fields, stats, footprints, Ability contracts, parameter constraints and family structure.
2. **Reference validation:** targets exist in the selected content set and satisfy the required family.
3. **Scenario/runtime validation:** placements, ownership, controllers, finite supply and current legal execution.

This extraction matters because current standalone Unit resolution does not
perform all structural checks, while scenario JSON parsing immediately creates a
game. Library validation must not manufacture a dummy board. See
[ScenarioDefinitionJson.cs](ProjectDelve.Engine/ScenarioDefinitionJson.cs).

Use concrete typed dependency extraction for:

- Loot rewards to Equipment or Item.
- Summon Adjacent to Unit Type.
- Summon Worker/Troll to their established target and Resource dependencies.
- Terrain assemblies to surface and Fixed Components.
- Production, storage and Machine operations to Resources.
- Deck compositions to card definitions.
- Placements and inventories to their definitions.

Mechanic schema reference annotations help, but are not a complete dependency
graph. Some dependencies are structural or implicit in concrete mechanics.

Build lookup tables before validating references. Self-summoning or mutually
referencing Units do not inherently constitute invalid cycles; closure traversal
needs a visited set. Reject cycles only where the underlying model forbids them,
such as unsupported Terrain assembly composition or future inheritance.

Diagnostics should identify package, definition, field and conflicting origins.
Replace current canonical-count-based path rebasing with provenance-aware
locations. See
[ScenarioValidationContext.cs](ProjectDelve.Engine/ScenarioValidationContext.cs).

**The Browser needs a new destination and scope model, not replacement forms.**

Minimal changes are:

- Add Canonical, Library and Scenario views.
- Allow Create in Library immediately from Home.
- Add a persistent library destination implementing the existing revision/context/save contract.
- Make post-save navigation and refresh independent of scenario snapshots.
- Show origin, revision, authored/effective status and dependency links.
- Offer Copy, Use in Scenario, Update Reference and promotion preview.
- Show missing references and collisions without silently substituting definitions.
- Revalidate on save; preserve scenario undo/redo and stale-editor protection.

The current adapter is already documented in
[CONTENT_DESIGNER.md](CONTENT_DESIGNER.md#scope-and-persistence). The host
assumptions are concentrated in
[content-browser.js](ProjectDelve.Web/wwwroot/content-browser.js).

Start with Equipment and Loot forms, which already exist. Other families can use
the package format before receiving dedicated editors. Eligibility, references and
parameter validation should continue coming from Engine; JavaScript should render
authoritative results.

**Empty Core requires several explicit decouplings.**

| Current assumption | Required change |
| --- | --- |
| Catalog resolvers and state constructors preload canonical content | Accept an explicit content set; distinguish omitted/default content from intentionally empty content. |
| Unit lookup can fetch canonical definitions during play | Runtime lookup must use the captured effective catalog exclusively. |
| Board and FoW assume canonical Stone Floor | Require explicit resolved terrain for playable boards; remove hidden canonical insertion. |
| Terrain validators consult current canonical rosters | Separate definition validity from canonical replacement policy and legacy conversion. |
| Resource production uses static Resources | Resolve required Resources through the effective context. |
| Startup creates a canonical playtest scenario | Allow Home, Browser and Designer to start without a game. |
| Designer assumes at least one Unit option | Provide empty states and disable only operations requiring unavailable content. |
| Empty Browser listings lose mechanic authoring contracts | Load Engine contracts independently of content entries. |
| 3D manifests require exact canonical Unit coverage | Permit missing/unrelated assets and use generic rendering. |

Relevant examples include [Model.cs](ProjectDelve.Engine/Model.cs),
[FogOfWar.cs](ProjectDelve.Engine/FogOfWar.cs) and
[unit-visuals.js](ProjectDelve.Web/wwwroot/unit-visuals.js).

An empty catalog should be valid. A particular playable scenario must still supply
its required terrain and content. Mechanics with established Worker, Troll,
Diamond or Banana dependencies should report missing dependencies when used; they
should not silently install standard content.

Generators can remain consumers of a declared standard content set. Missing
prerequisites should make a generator unavailable with an explanation. Adding
canonical content should not automatically alter its selection pools.

For future assets, retain separate presentation references to image, icon and
model identities. Existing Terrain recipes, visual manifests and generic fallbacks
provide extension points. Package metadata can later carry asset dependencies and
hashes. Missing optional art must not change gameplay validity. Asset upload and
management can remain entirely outside these milestones.

**Implementation should be split into bounded milestones.**

| Milestone | Deliverable | Acceptance boundary |
| --- | --- | --- |
| 1. Shared validation and dependencies | Extract definition validation, typed dependency queries and provenance diagnostics | Validate definitions without a board; cover missing references, legal summon cycles and inline variants |
| 2. Explicit composition | C# bundle adapter and effective catalog passed through creation/runtime lookup | Canonical behavior unchanged; delayed summon after save/load uses captured definition |
| 3. Package JSON and canonical provider | Shared serialization, package validation and additive canonical JSON loading | New canonical definition loads without C# generation; duplicate provider identities fail |
| 4. Library persistence | Immutable revisions, atomic manifest, conflict checks and import/export | Restart persistence, stale-write rejection and dependency-complete round-trips |
| 5. Standalone authoring | Library destination using Equipment/Loot forms; copy between scopes | Create without a scenario; empty-library UI; existing Designer recovery and undo preserved |
| 6. Scenario references | Pinned library imports, embedded closure and explicit updates | Library edit/removal does not change saved scenarios; Playtest, FoW and state round-trips agree |
| 7. Canonical promotion | Preview and publication with dependency batches | Conflict, stale-source and partial-write cases preserve valid catalogs and intended identities |
| 8. Additional families | One editor/validation integration per family | Typed references and round-trips verified independently; inline Resource/Machine semantics preserved |
| 9. Empty Core | Remove remaining startup, terrain, UI and asset assumptions | Empty startup works; an entirely authored content set can create a valid game |

Canonical C# migration should be incremental after the JSON provider exists: move
an entry or family, verify equivalent IDs, values and ordering, then remove its old
provider declaration. Do not keep duplicate authoritative definitions.

Each implementation milestone should add focused tests before broader
verification. Preserve the five established campaign scenarios through
loading/validation checks, without turning their mutable layouts into fixtures.
Protect both generator algorithms and random streams with dedicated fixtures.
Preserve current state serialization, continuations, physical counters and FoW
projections.

Content snapshots are necessary for deterministic replay, but do not guarantee
replay across changed Engine implementations. The Web reconnect journal is also
not a durable game-save system; this work should preserve existing replay behavior
without quietly promising a new one.

**The decisions worth approving before implementation are:**

- **Reference policy:** pinned, embedded library revisions as recommended; whether fully frozen canonical scenario snapshots should become an additional mode despite current live-preset semantics.
- **Identity policy:** retain family + Type ID uniqueness within an effective catalog, with explicit collisions; defer fully package-qualified runtime identities.
- **Promotion ownership:** preserve identity and archive/redirect the promoted source, rather than leave two independently editable owners.
- **Canonical updates:** distinguish adding new identities from explicitly replacing existing canonical definitions.
- **Library ownership:** initially use one application-local library, matching current persistence, or introduce separate user libraries.
- **First UI scope:** reuse Equipment/Loot editors first, then expand family by family.

No new gameplay mechanic or change to `CONSTRAINTS.md` is needed for the
recommended initial path.
