# Credits implementation report

## State and authoring

Each Side has a shared integer balance. A missing entry means zero. A ground pile holds a Cell and a positive amount. Scenario setup validates Side references and uses the same pile-merging rules as `GameState.PlaceCredits`. Both check bounds, positive amounts and whether any Side could collect every outstanding pile without exceeding 2,147,483,647 Credits. Placing a pile beneath a stationary Unit leaves it there until an arrival resolves. [Engine model and validation](ProjectDelve.Engine/Credits.cs)

The Scenario Designer exposes opening balances under **Sides & Objectives** and pile amounts in Cell properties. The optional JSON fields are `sideCredits` and `creditsPiles`. Both travel through DELVE1 and Designer export, import and draft recovery. Phase 1 introduced authored piles. Expedition now uses the same representation for procedurally placed piles. [Designer instructions](README.md#scenario-designer--phase-1) · [World Credits placement and visuals](WORLD_CREDITS_PLACEMENT_AND_VISUALS.md)

## Collection and visibility

The Engine uses the existing arrival queue. Hazards and their consequences resolve first. Each surviving arrival checks its current footprint in row order. Credits resolve before unopened Loot on the same Cell. The pile is removed and its amount credited in one resolution step. Stay, initial placement, summoning, Swap, external relocation and individual Run Rails steps use that same boundary. An ordinary Move checks its destination. Collection uses no Action, Bonus Action or RNG draw. [Rules](CONSTRAINTS.md#credits) · [Arrival implementation](ProjectDelve.Engine/LootResolution.cs)

The `CreditsCollected` event identifies the Unit, Side, Cell, amount and resulting balance. A Side observer sees its own total. Piles and collection events follow explored-Cell visibility. A visible opposing pickup can explain the amount collected while keeping the new total private, including when Fog of War is disabled. The full-information playtest table can inspect all balances. Gameplay, playback, reconnect and controller views consume Engine observations. [Observation rules](ProjectDelve.Engine/FogOfWar.cs)

## Expedition and compatibility

The completed source checkpoint supplies opening balances for every Side, including a Side with no surviving or deployed Unit. Only the new region receives the copied balances. Its opening piles come from its own scenario. The transition receipt and archive preserve the source balances, remaining piles and random continuation across retries. Saves and scenarios that predate Credits default to zero and an empty board pile list. Optional setup fields preserve the serialized form of existing scenario definitions and canonical Content digests. [Expedition handoff](EXPEDITION_ARCHITECTURE.md#side-credits-at-the-handoff)

## Verification

- Engine tests exercise scenario JSON and DELVE1, validation and overflow, shared and opposing balances, arrival boundaries, hazard and Loot order, multi-Cell footprints, action accounting, detached resolution steps and replay. [Engine tests](ProjectDelve.Engine.Tests/CreditsTests.cs)
- Web tests exercise observer and controller redaction, replay, exact RNG continuation after resume, legacy checkpoints, archived piles and repeated handoffs. [Projection tests](ProjectDelve.Web.Tests/FogOfWarProjectionTests.cs) · [Persistence tests](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs)
- Client tests cover Designer round-trips and editing, numeric pile display and the current Side's balance. A desktop and mobile browser walkthrough confirmed a 7-Credit collection changed the observing Side's balance from 4 to 11 and left a separate 13-Credit pile visible. [Designer tests](ProjectDelve.Web.Tests/credits-ui.test.cjs) · [Gameplay tests](ProjectDelve.Web.Tests/door-choice.test.cjs)

The solution built successfully. The full Engine suite, including generation, passed 3,808 tests. The Web suite passed 888. The client suite passed 1,141 and skipped its optional Beacon benchmark. Edge also rendered a 13-Credit pile on a Cell shared with Coal, a Banana Peel and unopened Loot. As with other tabletop pieces, a figure or nameplate can obscure part of a ground label at some camera angles.
