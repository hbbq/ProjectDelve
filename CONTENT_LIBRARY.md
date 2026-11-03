# Content Workspace

There is one application-local mutable Workspace. It stores complete typed content definitions in `App_Data/ContentWorkspace/workspace.json`, outside wwwroot. Configure `ContentWorkspace:Path` to select its directory. There are no packages, dependency snapshots, immutable revisions, or pins in the Workspace authoring model.

GET `/api/content-workspace` returns `{token, definitions}`. PUT replaces the bundle using `{expectedToken, definitions}`; DELETE clears it using `{expectedToken}`. A stale token returns 409. Validation runs against Canonical plus Workspace before persistence. Writes flush to a temporary sibling and atomically replace the document under a file lock; restart reloads the document. Empty Workspace is valid. GET `/api/content-workspace/catalog` provides generated previews and origins.

Content Browser offers Canonical, Workspace and Scenario-local contexts. Edit in Workspace preserves a canonical Type ID; Create Copy starts an independent Type ID. Existing Unit, Equipment, Item and Loot Card forms supply previews and diagnostics. Workspace definitions can be edited and deleted individually.

The former Library test package `equipments-1` and its revisions were disposable and removed after inspection. Legacy Library services and tests remain temporarily available for internal tooling, but are absent from the Browser authoring workflow. No migration of historical revisions is provided.
