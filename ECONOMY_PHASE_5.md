# Economy Phase 5 — Paid Station upgrades

A DelveMart accepts an upgrade-only proposal or any combination of transfers, sales and upgrades. One successful Confirm spends one Action. The operator can reset or cancel the draft for free. [DelveMart rules](CONSTRAINTS.md#delvemart)

## Prices and physical targets

| Destination | Price | Effect in the workshop |
| --- | ---: | --- |
| Barbarian Level 2 | 24 Credits | 6 base HP, Bonus Action Friendly Swap and three native Rage Uses |
| Lucky Boots Level 2 | 12 Credits | +2 MOV, +1 maximum HP and Second Step with one Use |

Each additional Unit or Equipment Level can set a nullable 32-bit integer `upgradeCost`. A missing price leaves the Level valid for play, placement and restoration and makes it unavailable at the Station. Explicit zero permits an upgrade for zero Credits on a normal Confirm. The Content editor, typed validation, DELVE1 and content identity distinguish these values. There is no Level 1 price. [Designer](CONTENT_DESIGNER.md#authoring-and-inspecting-levels) · [Equipment content](EQUIPMENT_CONTENT.md#physical-levels)

The Unit pool includes living, placed Units on the operator's Side regardless of inventory capability or Unique status. Each retained physical Equipment instance can advance independently, including during transfer. Each target can advance one Level per Confirm. The existing Station rules determine object eligibility, ownership and terrain occupancy.

## One provisional transaction

The typed allocation adds optional `upgradedUnitIds` and `upgradedEquipmentIds` arrays to the retained assignments and sales. Clients supply instance IDs. The Engine computes current offers, checks for duplicate or incompatible selections, and resolves the final inventories and Levels on a copy. Both preview and Confirm evaluate that final proposal. The calculation uses 64-bit totals and validates the settled balance and outstanding pile headroom before publication.

Pricing, preview and settlement consume no RNG. Once Confirm succeeds, the ordinary activation loop resumes and can draw the next activation token when the rules require it.

For example, the workshop starts with 34 Credits. Selecting both upgrades costs 36. Selling one Second Wind earns the remaining 2 and makes the proposal affordable. Confirm removes that Item, upgrades the selected figures/cards and leaves zero Credits. The Barbarian heals to 7 HP when keeping the upgraded Boots. Transferring those Boots instead gives their new owner a higher maximum without healing them. A non-Unique bearer retains the 1 HP cap. [Playable workshop](VISION_AND_MACHINES.md#play-the-workshop)

An upgraded Unit refreshes its native Ability Uses. An upgraded Equipment instance refreshes its own Uses. Other sources keep their counters. The opening Operate pays its ordinary Action, costs and use exactly once. A physical Single Use Item that supplied Operate is consumed. If the upgraded invoking source retains the same limited Operate key, one use remains spent on that source, including after a transfer. If limited `crank` becomes limited `console` with two Uses, `console` starts at 2/2. An unlimited opening Operate has no spent use to preserve. [Ability authoring](ABILITY_CONTENT.md#instance-levels-and-ability-sources)

## Observation, saving and the region boundary

The owner sees current and proposed HP, Levels, neutral loadout comparisons, source-specific Abilities and Uses, proceeds, costs and balance. The Station event records the selected targets and destination prices. The operating Side and full-information observers can see the accounting. Other Sides receive only information permitted by the existing observation rules.

An Expedition checkpoint can retain an unfinished proposal, including one that cannot yet be confirmed. Resume binds it to a fresh decision. Confirm revalidates the prices, Side balance, source and final placement. Duplicate command IDs reuse the original result only for the same allocation. The settled Levels, ownership and Credits then cross the normal region handoff. The successor applies ordinary arrival restoration. Reopening a materialized region keeps its captured definitions and prices. [Expedition handoff](EXPEDITION_ARCHITECTURE.md#levels-at-region-handoff)

## Browser verification

The real Edge walkthrough started each purchase from the DelveMart Workshop. The desktop and narrow views agreed with the Engine preview after Confirm.

| Operation | Final Credits | Observed result |
| --- | ---: | --- |
| Upgrade only the Barbarian | 10 | Level 2, 6/6 HP, Rage 3/3. [Unit preview](docs/economy-phase-5/unit-upgrade.png) |
| Upgrade only the Barbarian's Lucky Boots | 22 | Barbarian stayed at 5 HP with maximum 6. Second Step had 1/1 Use. [Equipment preview](docs/economy-phase-5/equipment-upgrade.png) |
| Sell one Second Wind and buy both upgrades | 0 | Barbarian reached 7/7 HP. The other Second Wind and partially spent Berserker Ring remained. [Combined preview](docs/economy-phase-5/combined-preview.png) · [Committed result](docs/economy-phase-5/combined-result.png) |
| Give the Boots to the Cleric and upgrade them on mobile | 22 | Barbarian ended at 5/5. The Lying Cleric received the upgraded Boots and ended at 2/5. [Mobile proposal](docs/economy-phase-5/mobile-transfer.png) · [Committed result](docs/economy-phase-5/mobile-result.png) |

Seven browser runs also covered price authoring and inspecting the same Ability key before and after an upgrade. The opening Barbarian shows Action Friendly Swap and Rage with two Uses. Its proposed Level shows Bonus Action Friendly Swap and Rage with three Uses. Browser reports recorded no console errors or failed requests. [Opening Abilities](docs/economy-phase-5/opening-abilities.png) · [Proposed Abilities](docs/economy-phase-5/proposed-abilities.png)

## Integration boundary for Phase 6

Paid upgrades cover living, placed Units and retained Equipment already in the operation. Recruitment would introduce a new physical Unit, a placement decision and permanent Party membership. It can use a separate Confirm at the same Machine. This phase supplies no recruitment or placement command.

## Limits and test coverage

The two published prices are provisional. The browser walkthrough verifies transaction arithmetic and presentation. Expedition runs over multiple regions will be needed to judge how frequently players can afford each upgrade. The preview uses the existing isolated, neutral stat comparison. Its HP line reports actual current and projected HP, and Confirm uses live terrain occupancy.

Engine tests exercise source payment, changed and retained Operate keys, zero and omitted prices, overflow, malformed proposals, sale-funded combined settlement, non-Unique HP, final occupancy, source-specific restoration and deterministic replay from a serialized pending decision. Web tests exercise authorization, stale and duplicate commands, invalid draft restoration, DELVE1, price identity and region handoff retries. A historical-checkpoint test reopens unpriced definitions against a priced catalog and verifies that prices refresh in the successor. Client tests cover normal and legacy pending rosters, source comparisons, price authoring and mobile controls.

| Final verification | Result |
| --- | --- |
| Engine, including generation and Expedition rules | 3,888 passed |
| Web, including Designer, Expedition persistence and API | 904 passed |
| Node client and browser dispatcher | 1,175 passed, one optional Beacon benchmark skipped |
| Real Edge | Seven runs, zero console errors or failed requests |

The .NET 10 solution built with restored dependencies. NuGet issued warning `NU1900` because its package vulnerability feed was unreachable during verification.
