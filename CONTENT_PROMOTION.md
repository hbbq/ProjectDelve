# Promote Workspace to Canonical

Content Browser's **Workspace Management** provides family/search filtering and
multi-selection for this operation. Preview lists additions, replacements, required
Workspace dependencies and existing Canonical dependency reuse. Workspace overrides
of dependencies remain optional when the resulting Canonical catalog validates.
Confirmation publishes exactly the selected identities, then removes those identities
from Workspace. Unselected definitions remain in Workspace.

POST `/api/content-workspace/promotion/preview` accepts `{expectedToken, selected: [{family, typeId}]}`. The response identifies new definitions, replacements, and missing Workspace dependencies. Only explicitly selected definitions publish. Include missing dependencies explicitly and preview the expanded batch before confirming.

POST `/api/content-workspace/promotion/publish` accepts `{preview, confirm: true}`. The preview includes the Workspace token and canonical fingerprint; both are rechecked. The server rebuilds the proposal and validates the complete resulting canonical catalog and reference closure. A stale or altered preview fails.

Publication atomically writes `workspace-publication.bundle` in the canonical directory. This JSON envelope holds typed definitions and an explicit ownership-transfer manifest. It replaces C# or JSON owned identities in the effective provider catalog without rewriting C# rosters or generating C#. Replaced source definitions are excluded, so the effective catalog has one authority per identity. New IDs become canonical JSON content.

Publication holds Workspace then canonical storage locks. The canonical file commits first; Workspace cleanup commits afterward. Failure before publication leaves both unchanged. Failure during cleanup retains the authored Workspace definitions and the published canonical definitions; reload, preview, and retry safely. Successful publication removes only the selected Workspace definitions.
