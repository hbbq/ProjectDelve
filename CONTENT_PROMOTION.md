# Promote content to Canonical

Promotion publishes typed definitions into a new, version-1 JSON package in the
configured `CanonicalContent:Directory`. It generates no C# and adds no gameplay
execution paths. Existing C# and JSON providers still pass through the same Engine
composition and dependency validation.

## Browser workflow

1. Open Content Browser from Home, choose Library and open a saved revision, or
   inspect a Scenario Designer draft containing local or pinned content.
2. Select Equipment or a Loot Card and choose **Promote to Canonical**. Unsaved
   Library work must be saved first. Historical immutable revisions are supported.
3. Enter a new canonical package ID. Select additional Equipment/Loot definitions
   from the same source if desired. Unrelated definitions are not selected.
4. Choose **Preview promotion**. Engine supplies the typed dependency closure,
   dependencies already satisfied by Canonical, additional definitions to publish,
   source revision/digest, canonical destination revision and resulting package JSON.
   Missing references, invalid mechanics and identity conflicts prevent publication.
5. Choose **Confirm and publish to Canonical** explicitly. Successful publication
   refreshes canonical Browser/Designer metadata and new-game creation immediately.

Canonical content remains read-only. Promotion creates packages; it does not
replace existing canonical identities or edit existing packages. A conflicting
selected identity fails even if its payload is identical, including identities
owned by C# rosters. A canonical dependency can be reused only when its typed
payload matches the source dependency. A different dependency payload is a
conflict, not an implicit canonical override.

## Service API

All routes use the existing Content Library request size limit, strict typed JSON
parser, no-store responses and validation diagnostic infrastructure. No route
accepts a filesystem path.

`POST /api/content-library/promotion/preview`:

```json
{
  "source": { "kind": "library", "packageId": "knightly-kit", "revision": 1 },
  "destinationPackageId": "knightly-canonical",
  "selected": [{ "family": "LootCard", "typeId": "knightly-gift" }]
}
```

For scenario content, use `source.kind` of `scenario-local` or `scenario-pinned`
and supply `source.scenario` containing the authored ScenarioDefinition. A playable
board is unnecessary for definition validation. Roots must belong to the requested
scope. Dependencies can come from the complete effective source catalog.

The response is a `PromotionPreview`, including the request, source digest,
source head, destination revision, closure, reused canonical identities, additional
identities, proposed ContentPackage and preview digest. Preview writes no package.

`POST /api/content-library/promotion/publish` accepts:

```json
{ "preview": "the complete preview response object", "confirm": true }
```

Replace the illustrative string with the actual object. `ContentPromotionService`
recomputes the complete proposal; client-supplied package payloads are never trusted
as publication input. Changed source/destination revisions or preview digests return
409 and require another preview and explicit confirmation. For Library sources,
the immutable selected revision and current head are checked under the Library
store lock throughout publication. Selecting an old revision is intentional, but
concurrent changes to its package head invalidate its preview. For browser-owned
scenario drafts, a source snapshot digest and the Designer destination revision
protect the preview; there is no server-owned mutable scenario draft.

The result includes the new package ID, canonical catalog revision and published
identities. Catalog revisions are deterministic SHA-256 digests of the complete
ordered canonical provider selection, including C# content. Replacing canonical
content remains a separate, unimplemented operation.

## Publication, ownership and isolation

Each publication is one new JSON package containing all newly required definitions.
Its `dependencies` identify the existing canonical providers that satisfy external
references. Optional package-level `publications` rows record each definition's
identity, payload digest, source kind/digest and Library origin when available.
These rows are provenance, outside gameplay definitions. Engine validates their
identity/payload correspondence. They are integrity metadata, not signatures or
an authentication mechanism.

Existing Library revisions are never changed, removed or rewritten. Published
identities are read-only in retained Library sources; unchanged definitions can
remain in a later package revision while unrelated content is edited. Changing a
published identity requires **Copy as new** with a new Type ID. Canonical editing
or replacement is not provided through Library Save.

Existing scenario pins keep their embedded definitions, digests and source revisions.
Publication does not rewrite scenario references or scenario-local arrays. Engine
recognizes an exact previously published payload when composing its retained
scenario snapshot with Canonical, registering one effective identity. This narrow
publication relationship does not allow arbitrary provider duplicates or a changed
Library definition to override Canonical. Explicit scenario-local Unit override
semantics remain intact. Historical local Loot collision handling remains intact.

Running games read only their captured snapshots. Authoring and new-game adapters
obtain the current canonical provider selection. Save/resume, physical Equipment
counters and replay do not consult the publication service or package files.

## Atomic persistence and recovery

The publisher takes an exclusive canonical directory lock, reloads all providers,
checks the expected catalog revision, and validates the entire proposed catalog.
It writes and flushes a temporary file beside a server-generated final filename,
then atomically renames it to `publication-<server-generated-id>.json`. No existing
package file is overwritten. A reader sees either the old catalog or the complete
new package, never a partly published dependency group. The host swaps its
immutable authoring provider selection after commit.

Temporary `*.json.tmp` files from an interruption are ignored by package loading.
A failure before rename leaves the previous catalog authoritative; publication can
be retried from its preview if revisions still match. If the response is lost after
commit, inspect canonical packages before retrying: the destination ID is now owned
and another publication cannot silently overwrite it. Filesystem paths remain
server-only. Junctions and symbolic links under the configured directory are
rejected. Provision write access to that directory for local publication.

Out-of-band package file edits require a host restart to refresh other authoring
requests; preview/publication always reload and validate disk under their lock.
Multiple independent hosts are not a live shared registry.

## Limits and verification

Browser root selection covers Equipment and Loot Cards; established typed
dependencies are discovered by Engine. The service can select other supported typed
package definitions, but introduces no additional family editors, global reusable
Machine/inline Resource catalog, or Cell Marker authoring. Scenario setup/deck
configuration is not promoted as reusable content. Existing Resource/Machine
limitations remain as described in CONTENT_VALIDATION.md.

There is no canonical replacement editor, unpublish/delete workflow, inheritance,
asset manager, C# migration or remote registry. Package provenance is application
metadata and not an untrusted-code execution boundary. This local application
retains its existing hosting/access model.

Focused tests cover Equipment and Loot closure, canonical dependency reuse,
C#/JSON identity rejection, scenario-local and pinned sources, stale source and
destination previews, interrupted atomic writes and restart recovery, read-only
published Library identities, Browser/Designer/API refresh, Playtest, portable
scenario transports and deterministic game save/resume. Client tests cover group
selection, explicit confirmation, stale drafts, failures and saved revision selection.

Milestone 7 verification: the solution build completed with zero warnings/errors;
all 2,189 Engine tests, 592 Web tests and 860 JavaScript tests passed. Web test
collections were serialized using an isolated runner configuration, without changing
assertions, to avoid the previously observed parallel Gambit SSE timeout. That
configuration was removed after the run. The initial JavaScript run used a broken
default Python launcher; the successful full rerun used the documented
PROJECTDELVE_PYTHON installation.

Desktop/mobile Edge verified Library creation, Loot plus Equipment pinning and
scenario Save, dependency preview, explicit JSON publication, canonical visibility,
retained read-only Library sources, a new canonical-content Playtest and reopening
the original pinned scenario. The saved scenario definition remained identical
before/after promotion. Final inspection reports contained no console/request
errors. All publication QA used isolated data/canonical directories; checked-in
canonical content and the existing port-5080 host were preserved.
