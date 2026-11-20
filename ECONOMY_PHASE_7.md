# Economy Phase 7 — Paid Machines and early Expedition income

DelveCare heals the Unit that operates it. Meteor-O-Mat gives its operator a physical Last Resort Fireball. Both Machines are available in Scenario Designer and can appear in Expedition. Each purchase spends one Operate Action and draws from the operating Side's Credits. Opening the offer and cancelling are free. [Game rules](CONSTRAINTS.md#paid-machine-services) · [Gameplay controls](VISION_AND_MACHINES.md#paid-services-in-play)

| Service | Credits | What the operator receives |
| --- | ---: | --- |
| DelveCare · Minor Treatment | 10 | Up to 2 HP, capped at current Effective Max HP. Restoring 1 HP still costs 10. |
| DelveCare · Full Treatment | 50 | Restore to current Effective Max HP. Both treatments remain selectable when they would heal the same positive amount. |
| Meteor-O-Mat · Last Resort Fireball | 15 | One new physical copy of the existing `last-resort-fireball` Single Use Item, owned by the operator. |

## Operate, content and persistence

A Machine Operation can declare a list of services. Each service has a stable ID, display name, nonnegative whole-Credit price and one effect. The supported effects are fixed healing, full healing and giving the operator one physical Item of a referenced Type. Existing Machine definitions can omit services. In Designer, authors can place either Machine, edit its local prices and Minor Treatment amount, save, undo, export, reopen and playtest it. [Typed declarations](ProjectDelve.Engine/Machines.cs) · [Designer authoring](CONTENT_DESIGNER.md#paid-machine-placement)

Opening a Machine reserves the chosen Operate Ability. The Engine quotes each service and returns the operator's HP, effective maximum, Side balance, projected result and reasons an offer is unavailable. Confirm takes the service ID, rechecks the live source, Action, Machine, Side, price and eligibility, then commits payment and the effect together. A native or Equipment use is spent once. An Item supplying Operate is consumed before the purchased Item is granted. A rejected or stale Confirm changes nothing. An accepted command retry reuses its receipt. Accounting and purchased object IDs are private to the operating Side and the full-information table. Visible healing uses the ordinary healing event. [Resolution](ProjectDelve.Engine/PaidMachines.cs) · [Ability sources](ABILITY_CONTENT.md)

Treatment requires a living eligible operator who would regain HP. It preserves posture and Ability uses apart from any cost of the opening Operate Ability. The non-Unique Effective Max HP cap is 1. Vending requires the Item Type in the active content and an operator who can own Items. Each copy has a distinct deterministic physical ID and uses the Item's existing Ability, target selection, dice and consumption rules. The purchase itself draws no RNG. The Machine can sell another copy to a Unit already carrying one. [Item ownership and use](LOOT_CONTENT.md)

Scenarios capture the referenced Item definition even with an empty Loot deck. An authored Machine whose Item Type is missing displays the unavailable offer. Pending purchases survive save/resume and are requoted on Confirm. Completed purchases preserve the balance and owned Item through a checkpoint or region handoff. Region-qualified vendor IDs distinguish purchases across generated boards. The existing DelveMart keeps its separate Manage and Recruit decisions. [Expedition persistence](EXPEDITION_ARCHITECTURE.md#discoverable-paid-machines) · [Checkpoint, command retry and handoff test](ProjectDelve.Web.Tests/PaidMachineClientApiTests.cs)

## Expedition placement and economy

Each region independently requests each public service Machine with probability **1/12**. A request gets up to 96 attempts to find an accessible open 3 × 3 site after population and Loot placement. An unsuccessful request leaves the board alone. Generation uses separate seed streams and preserves the arrival DelveMart in successor regions. Existing materialized boards keep their saved Machines. Across 30 regions from five fixed seeds, DelveCare was requested twice and placed once. Meteor-O-Mat was requested three times and placed three times. [Placement policy and compatibility](EXPEDITION_ARCHITECTURE.md#discoverable-paid-machines)

In the nine regions entered by the deterministic play policy, defeated Monsters generated **9 Credits**. The Party collected **6**, other Sides collected **3**, and confirmed DelveMart sales earned **0**. The median closing Party balance was **0 Credits**, and the highest balance in any run was **3**. No run could afford Minor Treatment (10), Lucky Boots Level 2 (12), a Fireball (15), Barbarian Level 2 (24), Full Treatment (50) or The Tycoon (75). An earliest purchase region for those services or milestones could not be established from these runs. [Gameplay data](docs/economy-phase-7/gameplay-income.json) · [Machine placement data](docs/economy-phase-7/machine-frequency.json)

Four starting regions ended at the Exit with zero Credits. All five runs eventually lost the Party. The policy scavenges, engages discovered enemies, attempts sales and then heads for the Exit. It uses a reproducible combat roll sequence for each seed. Five seeds and one policy provide limited evidence about human progression. The detailed assessment separates ground drops, collection by Side, sales, retained sale value and hypothetical income from defeating every initial Monster. The shipped Fireball has no Sell Value and the existing repeatable summon targets have zero Credits chance. Custom offers and summon targets can change those economics. [Seed-level measurements, reproduction commands and farming analysis](ECONOMY_PHASE_7_ASSESSMENT.md)

Keep the provisional prices for the next playtest. Compare a player who actively collects and returns to the Station with the simulated policy, recording defeats, uncollected piles, return trips, confirmed sales and the first affordable purchase. The measured balances suggest testing access to income before tuning any individual price.

Default automated controllers pass over interactive purchases. A human controlling an eligible Unit of any Side can select a service. The assessment therefore measures opportunities and purchasing power separately from the browser walkthrough, which exercises actual spending.

## Verification

| Suite | Result |
| --- | --- |
| Engine and generation | 3,952 passed, including 33 paid Machine cases. |
| Web and Expedition | 917 passed. A further run of 61 purchase, handoff, checkpoint and Designer cases passed on the final build. |
| Browser client and QA dispatcher | 1,179 passed. The optional Beacon performance benchmark was skipped. |
| Economy assessment | Both opt-in exports passed. A second run reproduced all 9 gameplay rows and all 30 placement rows. |

The solution built with cached dependencies. NuGet could not reach its vulnerability feed and emitted warning `NU1900`.

Real Edge walkthroughs used an ordinary authored test scenario at **1360 × 900** and **390 × 844**. Starting at 1/5 HP and 80 Credits, the operator bought Minor Treatment, reaching 3/5 HP and 70 Credits. On mobile, both treatments projected 5/5 HP. Choosing Full Treatment left 20 Credits. Meteor-O-Mat then granted the operator the Fireball and left 5 Credits. Those purchases spent three Actions. The operator used the Item for its fourth Action, selected a target, rolled the ordinary attack and defence dice, and consumed the Item. Both browser runs recorded zero console errors and zero failed requests.

| Browser checkpoint | Screenshot |
| --- | --- |
| Minor and Full Treatment quotes | [Desktop](docs/economy-phase-7/delvecare.png) |
| Equal healing, distinct prices, visible Confirm and Cancel | [Mobile](docs/economy-phase-7/delvecare-mobile.png) |
| Operator, Item, price and projected balance | [Meteor-O-Mat](docs/economy-phase-7/meteor-o-mat.png) |
| Purchased Item on the operator's card | [Owned Fireball](docs/economy-phase-7/owned-fireball.png) |
| Ordinary Fireball use and attack roll | [Fireball cast](docs/economy-phase-7/fireball-cast.png) |

Automated coverage includes healing and source accounting, rejected purchases, deterministic continuation from a saved decision, Side-filtered receipts, published Item dependencies, stale and duplicate requests, checkpoint handoff and retry, Designer round trips and client interactions. [Engine tests](ProjectDelve.Engine.Tests/PaidMachineTests.cs) · [Web handoff](ProjectDelve.Web.Tests/PaidMachineClientApiTests.cs) · [Generation tests](ProjectDelve.Engine.Tests/ExpeditionEconomyGenerationTests.cs)
