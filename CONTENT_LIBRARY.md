# Persistent Content Library

The application-local Content Library stores independently authored
[JSON content packages](CONTENT_PACKAGES.md). It is separate from ScenarioLibrary,
canonical providers, scenario drafts and running games. No scenario or GameState
is created to save, validate, browse or export library content.

## Storage and revisions

The Web service is `ContentLibrary`. Its default directory is
`App_Data/ContentLibrary` beneath the Web content root. Configure another directory
with `--ContentLibrary:Path <directory>` or the equivalent .NET configuration key.
Storage is outside `wwwroot` and excluded from deployment by the existing
`App_Data` exclusion. Clients receive logical identities, never storage paths.

One versioned `library.json` document contains a stable application library ID,
package heads, archive flags and immutable revisions. Package identity is the
existing package ID; definitions keep their family/Type IDs. Each revision records:

- Origin `libraryId`, package, positive revision number and SHA-256 digest.
- Exact library dependency bindings (`packageId`, `revision`).
- The selected canonical provider packages captured when saving.

The digest covers the entire normalized revision, including metadata, bindings,
canonical context and origin, with the digest field empty. The digest detects
accidental modification; it is not an authentication signature. Serialization
uses the shared package options. Package heads, revisions and dependency bindings
have deterministic ordinal ordering; authored definition/Ability/deck arrays
retain meaningful order. No timestamps participate in revision identity.

Create uses `expectedRevision: 0`. Save requires the current positive revision;
success appends a new immutable revision and advances the head. Even an unchanged
save creates a revision. Stale saves return 409 with expected/current revisions.
Imported revisions preserve their origin, revision number and digest; subsequent
local edits use the destination library's origin and the next revision number.

Archive is explicit, conditional on the current revision, and hides a package from
ordinary listings. Current and historical reads/exports remain available, and
dependency bindings remain valid. Archived identities cannot be recreated or
edited. All historical revisions are retained; this milestone has no purge or
restore operation.

## Explicit dependencies and validation

`package.dependencies` uses the package format's stable IDs. Every direct library
dependency needs an exact revision in `bindings`. Canonical dependencies need no
library binding: the service captures the declared canonical packages and their
canonical dependency closure from the host's configured providers. `canonical-csharp`
identifies the C# provider; checked-in JSON providers use their package IDs.
Library package IDs cannot claim an installed canonical provider's package ID.

Only the selected revision closure and declared canonical context participate in
validation. Other installed library packages never become ambient definitions.
Definition collisions are checked within that explicit catalog. Independent
packages may contain competing Type IDs, but selecting them together fails;
there is no load-order shadowing or automatic renaming. Same-package references
need no external binding. Package dependencies and Type ID references are distinct.

Engine's `ContentPackageCatalog.Compose`, standalone validation and typed graph
remain authoritative. Missing definitions, incorrect families, invalid mechanic
parameters, invalid Terrain assemblies and undeclared dependencies produce the
existing diagnostics. Sources use logical `library:<id>@<revision>` or
`canonical:<id>` documents plus package, definition and field paths.

Selecting two revisions of one package in a closure fails with 409. Selecting
dependencies with incompatible captured canonical contexts also fails. Existing
revisions are read and exported using their captured context, even after current
canonical providers change or disappear. A deliberate new save captures the
current declared canonical context and validates the resulting closure.

`SaveBatch` and the transactions endpoint atomically create/save several packages.
This permits mutually dependent packages and legal mutually summoning Units.
Bindings name their exact proposed revision numbers (1 for new packages), with
all expected revisions checked in one transaction. No partially saved cycle is
visible. Terrain composition restrictions still apply.

## HTTP and service APIs

All routes start with `/api/content-library` and return `Cache-Control: no-store`.
These are authoring APIs, separate from public gameplay observations.

| Method and route | Operation |
| --- | --- |
| `GET /` | Library ID and active package summaries; `?includeArchived=true` includes archives |
| `POST /packages` | Create `{package, expectedRevision: 0, bindings: []}` |
| `GET /packages/{id}` | Current immutable revision; `?revision=N` selects history |
| `PUT /packages/{id}` | Save `{package, expectedRevision: N, bindings: [...]}`; ID must match |
| `GET /packages/{id}/revisions` | Retained revisions, in ascending revision order |
| `POST /packages/{id}/archive` | Archive `{expectedRevision: N}` |
| `POST /transactions` | Array of create/save requests; atomic, results sorted by package ID |
| `GET /packages/{id}/export` | Revision-preserving transfer archive; optional `?revision=N` |
| `GET /packages/{id}/export/package` | Portable plain JSON package with typed dependency closure |
| `POST /import` | Import a revision-preserving transfer archive |
| `POST /import/package` | Create from a plain JSON package using declared canonical context |

Optional bindings default to empty. A binding looks like
`{"packageId":"my-gear","revision":2}`. Plain packages that need library bindings
can be imported through `POST /packages` with explicit bindings instead.

Service equivalents are `Catalog`, `Create`, `Read`, `History`, `Save`, `SaveBatch`,
`Archive`, `Export`, `ExportPackage` and `Import`. `ContentLibraryJson` reads/writes
transfer archives; `ContentPackageJson` handles plain packages. `ContentPackageJson.CreateOptions`
also supplies shared typed serialization settings for provider/storage envelopes.
Returned objects are detached from storage and caller-owned mutable collections.

Validation failures return 400; Engine composition failures include a `diagnostics`
array. Missing package/revision reads return 404. Stale edits and identity/context
conflicts return 409. Corrupt or inaccessible committed storage returns 503.
Request bodies are limited to 8 MB. The store defaults to a 64 MB committed
document limit, including all history and captured canonical context.

## Import/export semantics

Transfer archives use `{formatVersion:1, root:{packageId,revision}, revisions:[...]}`.
They include the selected immutable revision and its pinned library dependency
closure, with each revision's captured canonical context. They do not include
unrelated packages or the entire edit history. Exporting an old revision selects
its old dependency closure. Import validates integrity and the complete closure
before one atomic commit. The destination has its own application library ID;
imported revision origins remain intact.

The root package must not already exist, including archived packages. An installed
dependency revision can be reused only if its identity and digest match exactly;
import never changes its head or history. Missing/different installed history is
a conflict. Imports do not append historical revisions to an existing package or
silently replace, rename or update it. To edit an existing package, use Save.

Portable package export uses the Engine dependency graph to include required
typed definitions and mechanic requirements in a single ordinary package. It
preserves Type IDs and parameters but intentionally has no library revision
identity. It can be validated with the Milestone 3 CLI. It cannot be composed
beside its original definitions when identities collide.

## Example

Start with `my-content.json`, a format-1 package with a new package ID and valid
definitions as shown in [CONTENT_PACKAGES.md](CONTENT_PACKAGES.md):

```powershell
$base = 'http://localhost:5080/api/content-library'
$package = Get-Content my-content.json -Raw | ConvertFrom-Json
$create = @{ package = $package; expectedRevision = 0; bindings = @() } | ConvertTo-Json -Depth 100
$first = Invoke-RestMethod "$base/packages" -Method Post -ContentType application/json -Body $create

# Edit the package metadata/definitions, then save against the captured revision.
$package.title = 'Revised title'
$save = @{ package = $package; expectedRevision = $first.revision; bindings = @() } | ConvertTo-Json -Depth 100
$second = Invoke-RestMethod "$base/packages/$($package.id)" -Method Put -ContentType application/json -Body $save
Invoke-RestMethod "$base/packages/$($package.id)?revision=1"
Invoke-RestMethod "$base/packages/$($package.id)/revisions"
Invoke-WebRequest "$base/packages/$($package.id)/export?revision=1" -OutFile revision-export.json
Invoke-WebRequest "$base/packages/$($package.id)/export/package" -OutFile portable-package.json
dotnet run --project ProjectDelve.Console -- content validate portable-package.json

# In a different/empty destination library, import without rewriting revision identity.
# Invoke-RestMethod "$base/import" -Method Post -ContentType application/json -Body (Get-Content revision-export.json -Raw)
```

## Atomicity, recovery and boundaries

Every operation obtains an exclusive file lock and reloads committed storage,
including operations from separate service instances. A write validates its entire
transaction, serializes a temporary sibling, flushes it to disk, and atomically
renames it over `library.json`. Revision records and heads commit together.
Failures before replacement leave the previous document intact. Orphan temporary
files from interruption are ignored, never promoted automatically. The committed
file's structure, revision references and digests are checked on every access.
Invalid data fails closed and is not overwritten. Restore a verified whole-file
backup while the host is stopped; no automatic corruption repair or silent reset
is attempted. The filesystem must support atomic same-directory rename.

The store deliberately retains all revisions and captured context in one file.
Large histories may eventually require compaction/storage changes; automatic
garbage collection is deferred so future references cannot lose their revisions.
Backups and cleanup of orphan temporary files remain application administration.

The standalone Equipment/Loot destination is documented in
[CONTENT_DESIGNER.md](CONTENT_DESIGNER.md). There is no scenario library reference/import,
canonical promotion, inheritance, asset management or multi-user permission model.
Saving library content cannot change canonical providers, scenario-local content,
scenario references or running game snapshots. Resources/Machines and Cell Markers
retain the limitations documented in CONTENT_PACKAGES.md. Future explicit
Use in Scenario and Promote operations can consume these revision APIs; standalone
Create Copy already uses the shared content destination.
