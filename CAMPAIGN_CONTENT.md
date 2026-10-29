# Local and bundled campaign content

Project Delve presents one library assembled from bundled content and local authoring data. Scenarios remain independent initial game setups. No active game, progression or campaign completion is stored in this library.

## Locations and deployment

Paths below are relative to `ProjectDelve.Web` (the application content root):

```text
Content/Library/
  library.json                   # Git-versioned scenarios and campaigns
  covers/<sha256>.png             # Git-versioned imported covers
App_Data/library/
  library.json                   # Local schema v2: records, overrides and deletions
  library.v1.backup.json          # Original v1 data retained during migration
  .library.lock                  # Exclusive local storage lock
  covers/<sha256>.png             # Local imported covers
```

The existing C# catalog supplies the 28 original bundled scenarios and the `test-scenarios` campaign. `Content/Library/library.json` supplies additional bundled records, or replaces code-supplied records with the same ID. Its schema is `{ "version": 1, "scenarios": [...], "campaigns": [...] }`, using the existing scenario/campaign record fields. Gameplay definitions stay inside scenario records; cover URLs are metadata outside the definitions.

`App_Data` is excluded from Git **and from build/publish output**. `Content/Library` is included in application output, including its cover PNGs. Do not copy an author's entire application working directory to deploy content.

For deployment, set `Library__Path` or pass `--Library:Path <absolute-directory>` to a durable, writable directory **outside the application release directory**. This is the entire local library directory, containing `library.json` and `covers/`. Use the same configured directory after upgrades. The default `App_Data/library` preserves existing development installations, but deleting or replacing that directory loses local work. Publishing does not package or back it up. The host must have write access to local storage and read access to bundled content. `Library__BundlePath` / `--Library:BundlePath` can override the bundled directory for isolated testing; normal deployments use `Content/Library`.

Back up the complete local directory, including covers, while the host is stopped. Also keep the Git revision/bundled catalog used with it. Restore the directory and configuration together. JSON replacement is atomic, and the host refuses invalid storage rather than silently resetting it. There is no automatic history or automatic cloud backup.

## Identity, overrides and deletion

New scenarios and campaigns are local. Opening bundled content and saving creates a local override with **the same ID**. The bundled original remains unchanged. Local records win over bundled records by ID; names never determine identity. A scenario's single cover is shared by every campaign reference.

Unmodified bundled records receive future bundled updates. An override remains local until explicitly restored or promoted. Source labels identify **Bundled**, **Local**, and **Local override**. Use **Restore bundled version** to discard an override; existing campaign references remain valid.

Deleting local-only content removes its record. Deleting bundled content hides it with a local deletion marker, so restart or a bundle update does not unexpectedly bring it back. The library's hidden-content controls restore it. Campaigns retain references to deleted/missing scenarios and show them as unavailable; remove the reference or restore the scenario. Deleting a campaign does not delete its scenarios or images.

Numeric revisions are retained for existing content. Change tokens additionally protect against bundled updates and restore operations. If a save conflicts, the current workspace remains intact; reopen the latest record or save a new copy. **Save as new** intentionally creates another ID.

On first access, V1 local storage is migrated with a backup of the original file. Exact untouched registered originals become bundled records. Edited or uncertain originals remain local overrides, preserving IDs and revisions. Previously deleted originals remain hidden. This avoids silently replacing authored data while allowing untouched content to receive updates.

## Designer and cover import

Designer **Save to library** and Campaign Designer **Save campaign** persist explicit saves. Browser recovery and **Save draft** remain separate from the shared library. **Test scenario** plays the current unsaved workspace and does not save or overwrite any library record; returning to Designer preserves the workspace.

Use **Import cover image** in Designer's Library & Export panel or in Campaign Designer. Choose a PNG, JPEG or WebP image up to 12 MB and 4096 × 4096 pixels. The browser scales it to at most 2048 pixels per side and converts it to PNG; the server independently validates its structure and scanlines, removes metadata and stores canonical PNG bytes. Covers are decorative only: use atmospheric illustrations, not board screenshots or hidden scenario information.

An import updates the draft cover URL; **save the scenario/campaign** to attach it to that record. Importing an image alone never saves gameplay content. Existing manual root-relative/HTTPS image URLs remain supported, but finished content should use imported images so it can ship independently of another server.

Stored image URLs are `/api/library/covers/<sha256>.png`. The endpoint resolves local or bundled images with the same immutable name. Promotion therefore does not change URLs. Imported files are at most 5 MB; the server accepts noninterlaced 8-bit RGB/RGBA PNG up to 4096 pixels per side (the picker handles conversion). Imported but unused images are retained deliberately; there is no automatic garbage collection that could remove a draft's cover. Include them in backups.

DELVE1 import/export still carries only gameplay definitions. It does not include names, IDs, campaigns or cover images. Draft files carry authoring metadata and cover references, but not image bytes. To move a complete local collection, copy the local library directory.

## Promoting a finished campaign

Promotion is an explicit development command, not a player-facing Publish button. First save the campaign, every referenced scenario and their cover choices. Obtain the campaign's stable ID from its browser link (`#campaign/<id>`). Review names/descriptions/covers for spoilers and image redistribution rights before committing them.

From the repository root, preview the exact campaign and dependencies:

```powershell
dotnet run --project ProjectDelve.Web -- --promote-campaign <campaign-id> --library-path ProjectDelve.Web/App_Data/library --bundle-path ProjectDelve.Web/Content/Library
```

Use the actual configured local directory if different. The preview makes no filesystem changes. It lists scenario IDs and imported cover filenames. Missing scenarios/images and unmanaged cover URLs are rejected; import external/manual cover images through the picker first.

Apply the same command with `--apply`:

```powershell
dotnet run --project ProjectDelve.Web -- --promote-campaign <campaign-id> --library-path ProjectDelve.Web/App_Data/library --bundle-path ProjectDelve.Web/Content/Library --apply
```

This upserts the campaign and its referenced scenarios into the bundled catalog, copies imported cover files, and retains IDs, revisions, ordering and URLs. Other bundled records are preserved. A repeated promotion of unchanged content produces the same catalog and files. Matching local overrides are released after the bundle is written; any concurrent local edits are retained. Existing open drafts may need to reopen after promotion because their change tokens are now stale.

Review the Git diff and commit `ProjectDelve.Web/Content/Library/library.json` plus the referenced `covers/*.png` files with the code change, if any. Local `App_Data` never belongs in the commit. Shared scenarios are promoted once by ID, even when several campaigns use them. Editing a shared bundled scenario affects every campaign that references it on installations without an override.

Publish normally after committing. A clean installation receives the C# originals and the bundled JSON/images, while existing installations retain local experiments. Promotion does not promise frozen engine rules across software versions; definitions still use the normal engine validation and canonical content boundary.
