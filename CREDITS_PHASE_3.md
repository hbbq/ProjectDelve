# Selling at DelveMarts

One Station Confirm can sell physical Equipment and Items, redistribute retained objects, or do both. It spends one Operate Action. A sale transfers Credits directly to the operator's Side. Cancel returns to the activation with the original inventories, balance and Action. [Station rules](CONSTRAINTS.md#delvemart)

## Prices for playtesting

| Type | Kind | Sell Value |
| --- | --- | ---: |
| Lucky Boots | Equipment | 4 Credits |
| Swim Tube | Equipment | 3 Credits |
| Second Wind | Single Use Item | 2 Credits |

These are provisional prices for Expedition playtesting. The other shipped Types have no price. A scenario author can set `sellValue` on an Equipment or Item Type. Omission makes the Type unsellable. An explicit zero permits removing the object for zero Credits. Each copy has the same price even if its uses are exhausted. The property is omitted from serialized definitions when absent, preserving historical payloads and digests. A configured value participates in content identity and travels through Designer save, recovery, playtest and DELVE1. [Content Designer](CONTENT_DESIGNER.md#delvemart-sell-value) · [Equipment roster](ProjectDelve.Engine/EquipmentRoster.cs) · [Item roster](ProjectDelve.Engine/ItemRoster.cs)

## What Confirm does

The existing pending Operate records the eligible Units and their opening inventory. The draft assigns each physical ID to a final owner or lists it in `soldObjectIds`. Ground objects and other Sides' inventories are outside the pool. A retained owner change or a sale is sufficient to Confirm. The browser displays the Engine's current balance, proceeds and resulting balance and sends only object IDs and ownership choices. [Station API and editor](VISION_AND_MACHINES.md#delvemart-interaction)

Confirm rebinds the invoking Ability, revalidates the pool, resolves current Type prices, and checks ownership, duplicate Equipment Types, live footprint occupancy and Credits headroom. The Engine sums proceeds using 64-bit arithmetic and checks that the operating Side can still collect all outstanding piles. It then applies Operate accounting, the final inventories and the balance on a copy. Sold instances leave play, including objects originally drawn as Loot. A Single Use Item that opened the Station stays reserved for ordinary consumption. Equipment that opened it can be transferred or sold after its use and Costs are paid. A rejected draft spends nothing and can be edited. [Engine implementation](ProjectDelve.Engine/EquipmentStation.cs)

The existing `MachineOperated` event records sold IDs, previous owners, prices, proceeds and balance in deterministic physical-ID order. Opposing Side projections omit the sale lines, proceeds and balance. The ordinary visibility of figures and their inventories still applies. Publishing or retrying a command adds no sale roll. Command IDs, session revisions and decision IDs protect Confirm against double payment. [Event filtering](ProjectDelve.Engine/FogOfWar.cs) · [Web command guards](ProjectDelve.Web/PlaytestGame.Machines.cs)

## Expedition and future work

A checkpoint can retain an invalid draft and reopen it at the same decision. A checkpoint predating sales retains its saved Type definitions with no prices. New ordinary scenarios that reference the live catalog receive the current values. The handoff carries the final Side balances and the originals' retained inventories. Only confirmed sales affect the archive and successor. New regions still create their own independent Loot supplies. [Expedition regression](ProjectDelve.Web.Tests/ExpeditionClientApiTests.cs) · [Checkpoint compatibility](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs)

The Station remains a human-managed interaction. The only price input is the Type's fixed Sell Value. Phase 5 can extend this Station draft and the same validation and Confirm boundary to handle an upgrade on a retained instance. There is no upgrade investment or purchase accounting in this phase.

## Verification

The .NET 10 solution built successfully using restored packages. The Engine and generation suite passed 3,852 tests, and the Web suite passed 896. The client suite passed 1,152 tests and skipped its optional Beacon benchmark. NuGet could not fetch vulnerability metadata during the build. [.NET test log](.playwright-cli/credits3-final-dotnet.log) · [Client test log](.playwright-cli/credits3-client.log)

- Engine tests exercise sale-only and mixed drafts, zero-value removal, missing prices, ineligible objects, duplicate copies and charges, Operate sources, final Water occupancy, overflow, live repricing, cancellation and replay. Reordering sales and transfers produces the same serialized result. [Station tests](ProjectDelve.Engine.Tests/EquipmentStationTests.cs)
- Web tests exercise saved invalid sale selections, stale requests, idempotent Confirm, private observations, exact RNG continuation, and continued and retried region handoff. Content tests distinguish missing and zero across validation, playtest, transport and digests. Client tests cover selection, restoration, focus, totals, Designer history and draft recovery. [Web Station tests](ProjectDelve.Web.Tests/ExpeditionClientApiTests.cs) · [Authoring tests](ProjectDelve.Web.Tests/ContentEditorApiTests.cs) · [Client Station tests](ProjectDelve.Web.Tests/equipment-station.test.cjs)
- In the bundled workshop, Edge at 1440 × 900 sold one Second Wind. The Heroes' 3 Credits became 5, the other copy stayed available, and the journal recorded one Operate Action. At 412 × 850 the same sale also transferred Lucky Boots to the Cleric. The mobile preview showed the Cleric's MOV increasing from 3 to 4. Both screenshots and journals were inspected. Neither run logged a console error or a failed request. [Desktop preview](.playwright-cli/browser-qa/inspection-Laco3K/sale-preview.png) · [Desktop result](.playwright-cli/browser-qa/inspection-Laco3K/sale-confirmed.png) · [Mobile preview](.playwright-cli/browser-qa/inspection-Df9wMx/mixed-preview.png) · [Mobile result](.playwright-cli/browser-qa/inspection-Df9wMx/mixed-confirmed.png)
