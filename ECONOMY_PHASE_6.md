# Economy Phase 6 — Recruitment, the Expedition Party and The Tycoon

At an Equipment Station, the Expedition Party's Side can recruit The Tycoon for **75 Credits**. Recruitment has its own Confirm. It spends one Action, pays the opening Operate source, deducts the price and gives the Party a permanent new member. Cancel is free. Inventory sales can fund a later recruitment after the sale has been confirmed. [Rules](CONSTRAINTS.md#equipment-station-recruitment) · [Station controls](VISION_AND_MACHINES.md#recruit-a-party-member)

## Content and placement

| Type | MOV | RNG | ATK | DEF | HP | Level | Recruitment |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| The Tycoon | 4 | 1 | 2 | 2 | 5 | 1 | 75 Credits |

The Tycoon is Unique, uses ordinary Human control, can Attack, Open Door and Operate, and can own Equipment and Items. It uses the existing procedural figure and Unit card. The founding roster and the Equipment-based Budget Barbarian summon retain their original roles.

A Type's optional `recruitmentCost` is a nonnegative 32-bit integer. Omission leaves the Type out of the offers. An explicit zero enables a free recruit at the normal Action cost. New Expedition regions capture priced Types and their content dependencies before play. The Designer, DELVE1 and content digests preserve the difference between an absent price and zero. [Authoring](CONTENT_DESIGNER.md#authoring-and-inspecting-levels)

The search starts from the operator's footprint. It follows open orthogonal routes of up to three steps through explored Cells. Walls, closed Doors, occupied Cells, blocking Machines and impassable terrain stop the search. It selects the legal destination with the shortest route, breaking ties by row and then column. For an ordinary one-Cell recruit, a completely surrounded operator cannot place a recruit farther away. Preview and Confirm use the same Engine search. The offer also reports capacity, affordability, Unique and supported deployment restrictions.

## Party and resolution

An Expedition's initial Scenario and live GameState carry an ordered roster of stable Unit instance IDs, the owning Side and its capacity. New Expeditions use the five original Heroes and capacity six. The Station appends a newly allocated ID in the same transition that creates its Unit. Defeat leaves that ID on the sheet. A defeated Unique Party member therefore still occupies its slot and prevents a duplicate recruit. Summoning never appends to the roster. [Engine recruitment](ProjectDelve.Engine/Recruitment.cs) · [Party preparation and transfer](ProjectDelve.Web/ExpeditionParty.cs)

Placement, identity allocation and payment consume no RNG. Normal arrival effects then resolve, including hazards, Credits and Loot. The new Unit enters under the existing Round Start bag rules. The Exit's Party selector evaluates at the ordinary Reach Area boundary and requires **any two living Party members**. Recruiting alone does not end an activation or force an early objective check.

The Station accepts a recruit Type ID separately from a Manage allocation. It requotes against the live game, including the opening Operate source and Side balance. Failed validation leaves the Station open. A duplicate command uses the original receipt, and stale session/decision bindings fail. Authorized observers see the roster and accounting. Other Sides receive only ordinary visible creation and Machine events.

## Checkpoints and region handoff

An Expedition can save its pending recruitment selection, reopen the Station and request a fresh preview. Checkpoints store at most one saved Station draft. A completed purchase persists the Unit, roster, balance and any ordinary arrival consequences together.

The successor requests enough deployment slots for the committed roster. At six, the planner reserves at least twelve deployment Cells and a nine-Cell maneuver core. It preserves the arrival Station, seam and required route. The region transition keeps each member's identity, Level and physical inventory, including Equipment IDs and Levels. All Party members, including defeated ones, return Upright at their restored effective maximum HP with refreshed Uses. Objects left behind and balances at the completion boundary remain in the archive. Retrying the same crossing returns its existing receipt. [Expedition architecture](EXPEDITION_ARCHITECTURE.md#levels-at-region-handoff)

Current checkpoints write version 2. On opening a version 1 checkpoint, the loader first verifies its digest and historical structure, then maps the founder IDs to the active Party and its Exit selector in memory. The Expedition head retains `OriginalHeroIds` as founder provenance. Already materialized Types retain their captured definitions. A legacy region can first offer The Tycoon after the next region refresh. Ordinary scenarios without a Party keep their existing selectors and Station behavior.

## Verification

| Suite | Result |
| --- | --- |
| Engine and generation | 3,911 passed. Coverage includes source payment, zero and missing prices, duplicate Unique Types, defeat, non-Party summons, impassable and occupied routes, placement ties, arrival effects and RNG continuation. |
| Web and Expedition | 912 passed. Coverage includes legacy checkpoint migration, private previews, saved Station selections, stale and repeated Confirm, six-member handoff and retry after a failed head write. |
| Browser-client JavaScript and browser QA dispatcher | 1,177 passed. The optional Beacon performance benchmark was skipped. |

The solution built with restored dependencies. NuGet could not reach its vulnerability feed and issued warning `NU1900`. [Engine cases](ProjectDelve.Engine.Tests/RecruitmentTests.cs) · [Web command and retry](ProjectDelve.Web.Tests/RecruitmentClientApiTests.cs) · [Checkpoint and handoff cases](ProjectDelve.Web.Tests/ExpeditionRecruitmentTests.cs)

Four real Edge runs covered desktop and a 390-pixel mobile viewport. The test-owned starting region supplied a 75-Credit pile and a nearby Exit so the runs could exercise ordinary gameplay through to a production-generated successor. Each completed run recorded zero console errors and zero failed requests.

| Browser check | Observed result | Screenshots |
| --- | --- | --- |
| Desktop purchase and resume | Saved with The Tycoon selected, reopened the Station, confirmed once, and observed one Action spent, Credits fall from 75 to 0 and Party size rise from 5/6 to 6/6. | [Five founders](docs/economy-phase-6/party-five.png) · [Recruit preview](docs/economy-phase-6/recruit-preview.png) · [After Confirm](docs/economy-phase-6/recruited.png) |
| Narrow screen | Inspected the offer and spawn Cell, confirmed the purchase and returned to the board with six members. | [Mobile preview](docs/economy-phase-6/mobile-preview.png) · [Mobile result](docs/economy-phase-6/mobile-recruited.png) |
| Ordinary activation | The Tycoon moved from (58,19) through (58,20) to (58,21) during its own activation. | [Tycoon movement](docs/economy-phase-6/tycoon-move.png) |
| Exit and handoff | The Tycoon and one founder completed the Exit. Continue opened region (1,0) before Round 1 with all six members and 0 Credits. | [Completed Exit](docs/economy-phase-6/party-exit.png) · [Six-member arrival](docs/economy-phase-6/six-member-handoff.png) |

## Limits and Phase 7

The shipped roster adds one recruit at a provisional price. The current Expedition landing supports single-Cell Party figures compatible with ordinary Land deployment. Changing a future region's content to an incompatible definition can still fail preparation before publication, leaving the completed source available for retry. Future services or vending Machines can use the existing Side Credits and source-bound Operate accounting. Their purchases have no need to modify Party membership.
