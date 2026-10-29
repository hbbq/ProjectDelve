# Tabletop gameplay overlays

`three-viewport.js` supplies observed, displayed-checkpoint data to the overlay
helpers. Candidate references remain authoritative; drawing never determines
legality, visibility, allegiance or camera composition. Flat keeps its existing
feedback. No GLB, gameplay, observation or manual camera behavior changes are needed.

The visual language uses charcoal enamel, ivory type and brass accents. Existing
Side hues identify bases and nameplate tabs; expanded plates also show Side text.
Turquoise boundaries and a faint wash mark movement, while terracotta corners
and a diamond distinguish attacks. Brass corners emphasize activation/focus;
preview edges add ivory. Combat links stop outside bases and end in an arrow.
Public Objectives use dashed perimeters and corner diamonds, without filling
unknown space. Offscreen Objective signposts retain accessible pan buttons.

Names and HP share a screen-space plate. Active, inspected and combat Units get
priority and expanded status; Lying remains explicit. Distant secondary names
shorten before disappearing, with damaged HP retained in compact plates.
`three-label-layout.js` uses projected anchors, approximate body rectangles and
panel bounds: three nearby rows, two lateral slots, then one centered below-base
slot. Compact widths precede hiding; important full plates remain prioritized.
Spatial bins bound local overlap checks. No per-label DOM measurement, physics
solver or continuous animation loop is used. Connectors identify displaced plates.

`three-tabletop-marks.js` deduplicates identical marks and builds up to three
instanced batches per feedback/area group: faint wash, dark keyline and color.
Authored action symbols remain separate. Shared geometry/materials and disposable
instances use the existing overlay pass; there is no additional rendering pass.

Visual labels remain pointer-transparent and hidden from the accessibility tree.
Existing keyboard Cell focus, descriptive labels, Unit cards and Board choices
continue to expose full information and submit unchanged candidate keys. Touch
targets, focus outlines, reduced motion and the shared dice tray remain intact.

Body rectangles are conservative approximations, not mesh occlusion. Very dense
scenes can still hide secondary labels; expanded panels can obscure board content.
Hover/focus and Board choices remain the reliable routes to complete information.
