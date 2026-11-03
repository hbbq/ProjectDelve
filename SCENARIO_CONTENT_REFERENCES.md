# Scenario content scopes

New scenarios resolve complete definitions by identity (Content Family plus Type ID): **Scenario > Workspace > Canonical**. A closer definition replaces the whole definition; fields never merge. References contain family and Type ID and resolve against the final effective catalog, including Equipment grants and Summoning. Duplicate identities within one scope fail even if another scope would hide them.

There is no pin or Update Reference workflow for new authoring. Existing reference serialization remains available internally while obsolete tooling is retired.

Designer retains local editing, undo/redo, recovery and Save/Open. New games use current effective content. Engine captures definitions at creation; saved and running games do not re-resolve against later Workspace changes.

Designer DELVE1 export embeds Workspace definitions and their effective dependency closure as ordinary scenario-local definitions. Unsupported Workspace preset/Resource/World Deck embedding reports a diagnostic rather than silently exporting broken dependencies. An empty Workspace preserves the original scenario representation.
