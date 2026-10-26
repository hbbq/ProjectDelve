# Dungeon generator V1 implementation and verification

Verified on 2026-10-26. See [README.md](README.md) for the complete DNG1 contract,
algorithm, configuration limits, Monster pool and tuning policy.

## Implemented vertical slice

1. **Functionality:** reproducible Heroes vs Monsters scenarios with exactly four
   canonical Heroes, configurable geometry, Doors, density, difficulty, Objectives,
   FoW and optional seeds. Outputs are normal authoritative scenario definitions.
2. **Solid Rock:** a separate terrain identity with Tree-equivalent movement,
   placement, Phase, ordinary attack LOS and exploration behavior. Serialization,
   validation, Designer palette, Classic and Enhanced 2D work through existing
   terrain properties. Purple-grey hatching is temporary; no final artwork was made.
3. **Architecture:** `ProjectDelve.Generation` references the engine. BSP regions
   contain excavated rooms; rock corridors connect child networks, and optional
   extra connections form loops. Walls separate incidental floor contacts. A
   separate flood checks every carved Cell and Door before engine validation.
   This uses the investigation's BSP/stream guidance with actual unexcavated rock,
   rather than an all-floor partition. No investigation artifact was present in
   this checkout; the prompt and current constraints supplied the guidance.
4. **Determinism:** strict canonical DNG1 prefixes and complete 64-bit-seed codes,
   PCG32 reference vectors, SHA-256 domain streams, deterministic retries, five
   initial regression fingerprints, and exact scenario/DELVE1 round trips.
   Encounter population and Monster composition have separate streams.
5. **Population:** the explicit nine-type Threat allowlist; band-first integer
   bell weighting; density independent of difficulty; empty rooms and portal
   clearance; at most one initial Shaman, with unchanged runtime summoning.
6. **Objectives and FoW:** existing Eliminate Enemies, Reach Area and Defeat Units
   evaluators with Side At Start selectors. Summoned enemies still count toward
   elimination. Hero FoW defaults on, Monster FoW stays off, and hidden Monsters
   continue activating. Structured mission outlines remain above Unknown Cells,
   independent of Waypoints, including observed playback snapshots.
7. **Workflows:** shared preview/direct-play generation path, stable/outdated
   previews, seed and code controls, exact Designer transfer, modified-source
   labels, direct unseen import, retained complete codes, unchanged-scenario
   restart, explicit reveal warning and DELVE1 export. Stale preview responses
   cannot replace newer results or Designer drafts.

## Automated verification

- Build: `dotnet build ProjectDelve.sln --no-restore --artifacts-path .playwright-cli`.
- .NET: `dotnet test ProjectDelve.sln --no-build --no-restore --artifacts-path .playwright-cli`.
  Engine: **1,549 passed**. Web: **369 passed**. Total: **1,918 passed**.
- Installed Node executable: `--test tools/test-browser-qa.cjs ProjectDelve.Web.Tests/*.test.cjs`.
  **381 passed**, including seven dispatcher checks.
- New generation suite: 36 cases, including PCG vectors, strict parsing,
  reproducibility fixtures, geometry, placement, density/Threat corpus, FoW,
  generated Objective completion and a summoned hostile preventing elimination.
- API integration: seven cases covering exact preview/direct-play equivalence,
  seedless prefix completion, restart, stale revisions, invalid requests, hidden
  activations, observed mission snapshots and Solid Rock metadata/serialization.
- Client checks exercise the actual preview/direct-play handlers, stale requests,
  retained preview transfer, invalid input, Designer source recovery, and mission
  rendering over Unknown Cells in both views with Waypoints disabled.

The deterministic 24-seed comparison generated 216 samples without failures or
retries: Monster totals 106/191/360 by density, mean Threat 1.62/2.24/3.04/3.75/4.30
by difficulty, mean destination distance 36.6, and initial explored fraction 6.7%.
Another 128 seeds vary geometry, plus boundary-size checks up to 50 × 50 / 24 rooms.

## Edge verification

All browser work used `.codex/browser-qa.ps1`, data-only plans and the isolated
port-5081 host. PNGs were opened and visually inspected. Successful final plans
reported no console errors or failed requests. Temporary QA processes were closed;
the existing port-5080 host was preserved.

Paths below are relative to the repository root and live in ignored QA artifacts:

| Evidence | Screenshot / snapshot |
| --- | --- |
| Final Enhanced preview | `.playwright-cli/browser-qa/inspection-XYTiOt/final-preview-enhanced.png` |
| Final Classic preview | `.playwright-cli/browser-qa/inspection-XYTiOt/final-preview-classic.png` |
| Final narrow preview | `.playwright-cli/browser-qa/inspection-XYTiOt/final-preview-narrow.png` |
| Final direct play / hidden map | `.playwright-cli/browser-qa/inspection-XYTiOt/final-unseen-enhanced.png` |
| Classic FoW, Waypoints off | `.playwright-cli/browser-qa/inspection-VZICzu/unseen-classic.png` |
| Narrow gameplay | `.playwright-cli/browser-qa/inspection-VZICzu/unseen-narrow.png` |
| Designer transfer | `.playwright-cli/browser-qa/inspection-lGYrN7/designer-original.png` |
| Modified Designer source | `.playwright-cli/browser-qa/inspection-lGYrN7/designer-modified.png` |
| Outdated preview | `.playwright-cli/browser-qa/inspection-lGYrN7/outdated.yaml` |
| Seedless prefix / restart / inspect code | `.playwright-cli/browser-qa/inspection-5SLOIL/completed-prefix.yaml`, `restart-code.yaml`, `inspected-code.yaml` |
| Live Hero activation | `.playwright-cli/browser-qa/inspection-f1cswN/active-round.png` |
| Movement expands exploration | `.playwright-cli/browser-qa/inspection-P6V35o/exploration.png` |

Earlier workflow captures precede the final separation of population and
composition streams; the XYTiOt captures show the finalized DNG1 output. Geometry,
FoW and workflow behavior are unchanged by that separation. A preview token offset
was corrected during visual QA. Unsupported selector syntax and an incorrect card
heading selector were rejected/timed out; supported selectors were then used.
The dispatcher and its restrictions were not changed.

## Limits and follow-up

V1 has one profile, 14–50 Cells per dimension and 2–24 rooms subject to capacity.
Connectivity is geometric, not a victory or balance guarantee. Extra routes are
limited by distinct room pairs. Final Solid Rock artwork, wider playtesting and
future-version tuning remain opportunities. DELVE1 is preferred for preservation;
there is no permanent historical-generator compatibility promise.

No gameplay rules were changed, and `CONSTRAINTS.md` was not modified. There are
no unresolved rules or design decisions blocking the implemented V1.
