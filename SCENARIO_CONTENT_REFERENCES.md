# Pinned scenario content references

Scenario Designer can explicitly use Units, Equipment and Loot Cards from the Content
Library. Import selects an immutable revision and embeds only the selected typed
definitions and their required dependency closure. It does not subscribe the
scenario to the current library head, import every package definition, or flatten
the content into editable scenario-local arrays.

## Workflow

1. Create/save Equipment and its Loot Card in Library using the shared editor.
2. Open a scenario in Designer and choose **Use library content**. This attaches
   that scenario as the destination and opens Library scope in Content Browser.
3. Select a saved package/revision and Unit, Equipment or Loot Card, then choose
   **Use in Scenario**. Save/cancel unsaved package work before importing.
4. Review the selected definitions and dependency changes. Loot imports its
   Equipment/Item reward dependency automatically. **Apply reference to scenario**
   makes one undoable change. Preview and Cancel leave the scenario untouched.
5. Save the scenario normally. Its Loot/inventory choices now include the pinned
   definitions. DELVE1, draft files, recovery, scenario storage and Playtest retain
   the embedded content.

Entries show **Library pinned**, package and revision. They are read-only;
**Copy as new** creates an independent scenario-local definition with a new ID.
The original pin remains intact. Scenario-local Unit overrides remain supported.
Generated effective metadata and descriptions come from Engine, including the
normal override semantics. No package-qualified runtime Type IDs are introduced.

Editing Library content never changes existing scenarios or games. In the
scenario's Content view, choose **Update Reference…** on the pinned package/entry.
Choose an available revision and **Preview update**. The preview identifies added,
removed, changed and unchanged definitions/dependencies; changed content includes
Engine-generated effective descriptions and expandable before/after JSON.
Only **Apply reference to scenario** changes the pin, as one undoable operation.
An outstanding preview rejects application if the destination scenario changed.
Updates may deliberately select an older revision too; there is no automatic merge
or latest-revision tracking.

**View Library provenance** opens the source package/revision where available.
If the original library is unavailable, the provenance/update operation reports
that limitation. Opening, validating, saving or playing the embedded scenario
requires no library access. Archiving a library package retains its immutable
revisions and does not invalidate existing pins.

## Scenario format

The optional `contentReferences` array contains `ScenarioContentReference` records:

| Field | Meaning |
| --- | --- |
| `formatVersion` | Currently 1; unsupported versions fail validation |
| `libraryId`, `packageId`, `revision` | Original application library and exact package revision |
| `revisionDigest` | Immutable source revision identity |
| `selected` | Authored selection of family/Type ID identities |
| `closure` | Deterministic typed transitive closure, including selections |
| `origins` | Definition identities and their exact owning library revisions or captured canonical providers |
| `snapshot` | Minimal resolved `ContentBundle` required by that selection |
| `snapshotDigest` | SHA-256 over the normalized reference with this field empty |

The full source package need not be embedded. Revision identity identifies the
source; the snapshot digest protects the embedded selection, its dependency
metadata and provenance. These are integrity checks, not authentication signatures.
Definitions retain stable Type IDs and owner-local Ability keys. Provenance belongs
to the scenario container, never to an Equipment, Loot Card or Unit definition.

Same-package selections at the same revision merge explicitly into one reference
when using more content. Different pins can reuse an identical immutable dependency
once. Incompatible revisions of a package or conflicting family/Type IDs fail
validation; load order never chooses a winner. Selecting another revision of an
already pinned package requires Update Reference rather than Use in Scenario.

## Engine composition

`ScenarioContentReferences.Validate` validates format, digests, ownership,
standalone definitions, complete closure and extraneous data using the shared
composition/dependency graph. `Compose` assembles explicitly supplied canonical
content and embedded pins; `ScenarioCatalog` additionally applies local definitions
and established legacy Loot import compatibility, without a board or GameState.
Scenario creation uses the same boundary before resolving live Loot presets.

Required canonical dependencies are captured too. Their provenance explicitly
selects the embedded canonical payload for those identities, replacing that part
of the current canonical provider selection. This makes a pin independent of
later provider changes. Library-owned definitions still cannot overwrite a
canonical identity: they collide normally. Two pins with incompatible captured
definitions conflict. Only the established separate scenario-local Unit override
can replace a provider Unit. Named Resources and embedded Resource/Machine
variants keep their existing model restrictions.

Snapshot validation reports family, Type ID, field and logical package/revision
locations, such as `contentReferences[0].snapshot` and `library:knightly-kit@1`.
Missing typed dependencies, broken snapshots, unsupported versions and incompatible
source revisions are rejected. Self/mutual Unit summoning remains cycle-safe;
Terrain assembly restrictions remain unchanged.

Game creation captures the resulting effective definitions into the existing
GameState catalogs. Runtime rules, save/resume and replay use those catalogs;
neither scenario provenance nor Library storage participates in gameplay lookup.
Reference metadata is absent from GameState and public gameplay observations.
Existing Fog of War projection still filters hidden definitions and rewards.

## Web APIs

`POST /api/content-library/scenario-reference/preview` accepts:

```json
{
  "scenario": { "...": "ordinary ScenarioDefinition, possibly an incomplete draft" },
  "packageId": "knightly-kit",
  "revision": 1,
  "selected": [{ "family": "LootCard", "typeId": "knightly-gift" }]
}
```

It returns `reference`, the complete proposed `references` collection, typed
`changes` with before/after snapshots, and an effective authoring `catalog`.
Only Unit/Equipment/Loot selections owned by that package revision are accepted.
The Engine determines the closure; clients do not resolve or copy dependency rules.
Preview performs no scenario/game or package mutation and needs no playable board.

For an update, supply `expectedReferenceDigest` equal to the current reference's
snapshot digest, the desired exact `revision`, and the existing `selected` values.
An empty selection on update retains the prior selection. Missing/stale reference
identity fails instead of replacing an unrelated pin. The client applies the
returned collection through the scenario destination with its opening draft
revision, producing one history/recovery transaction. There is no server-owned
mutable draft or separate automatic apply endpoint.

The existing `/api/content/context`, `/api/content/validate` and Designer metadata
requests accept `contentReferences`. They validate against embedded definitions
without reading Library storage and supply the same reference pickers/previews as
scenario-local editing. Full scenario validation, import/export, saved scenarios
and Playtest use the standard ScenarioDefinition pipeline.

## Limits and verification

Import selection UI is limited to Units, Equipment and Loot Cards; their established
typed dependencies can include other families. No inheritance, asset management, pin removal editor or batch scenario-reference
update is introduced. [Promotion](CONTENT_PROMOTION.md) preserves these snapshots
and recognizes exact published payloads when composing the retained pin.
An update that conflicts with another pinned revision is rejected; coordinated
multi-reference changes require a future explicit batch workflow. Existing host
scenario/transport size limits still apply to embedded snapshots.

Focused .NET tests cover selective Equipment/Loot import, immutable revisions,
explicit update previews, shared dependencies and mutual summons, local Unit
overrides, collisions, broken snapshots, unavailable/archived libraries, scenario
storage and DELVE1, physical counters, replay and FoW. Client tests cover
preview-before-apply, no automatic updates, stable read-only pins and single-step
undo/redo/recovery. Desktop/mobile Edge inspection covers creation in Library,
scenario import/save, later Library editing, unchanged old pins, explicit update
and the updated Equipment in Playtest.

Milestone verification: build succeeded with no warnings; 2,189 Engine tests,
583 Web tests and 856 JavaScript tests passed. The full Web suite passed with
test collections serialized after the normal parallel run timed out in the
Gambit HTTP SSE test; that test also passed in isolation. Desktop/mobile Edge
inspection reported no console or request errors.
