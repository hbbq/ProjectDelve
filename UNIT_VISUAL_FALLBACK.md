# Generic Unit visual fallback

Unit visual metadata is optional presentation information. A valid Unit Type
does not need an entry in `art/models/unit-visuals.json` or `board-visuals.js`.
Registered portraits, models and authored dimensions continue to take precedence;
registered metadata is still validated for dimensions, references and asset safety.

Without a registered model, Tabletop uses the shared procedural, double-sided
round token, including when Miniatures is selected. Without a portrait, its face
shows up to two initials from the observed Unit's display name. Flat 2D uses its
existing circular token with the same identity marker. No fallback assets or
network downloads are needed. The Side-colored rim, reverse-face Lying marks,
observed name/HP plaques, selection, targeting and authoritative occupied Cells
use the normal renderer paths. Footprint size comes from projected figure
geometry, rather than the optional asset registry.

This applies equally to C#, Canonical JSON, Workspace and scenario definitions,
and to new Types entering play through Summon after renderer initialization.
Only server-observed figures and metadata are rendered; the fallback does not
enumerate content definitions to discover or reveal hidden Units.

The fallback is a token, not an anatomical miniature. Initials can coincide;
full display-name plaques and the Unit card provide additional identity. It
inherits the existing camera framing and label level-of-detail behavior on
desktop and mobile. Invalid registered metadata, unavailable catalog endpoints
and unsupported WebGL remain separate initialization errors.

## Verification

- `unit-visuals.test.cjs`: optional metadata, renderer catalog loading and retained
  registrations.
- `three-viewport.test.cjs`: unregistered Types, Side, posture, 2x2 picking,
  targeting, later arrivals and observation removal at desktop and narrow widths.
- Existing .NET Unit authoring and projection tests cover custom Summon,
  content origins and authoritative Fog of War filtering.
- Edge: test-owned imported scenario with Budget Barbarian (Upright/Lying,
  blue/red) and Budget Giant (2x2), at 1440x1000 and 390x844; no console errors
  or failed requests. Small screens retain the existing camera controls for
  panning/zooming when the current view clips a token at the viewport edge.

See also [Three.js viewport](THREE_VIEWPORT.md) and
[browser inspection](BROWSER_INSPECTION.md).
