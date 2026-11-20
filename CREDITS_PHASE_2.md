# Exclusive defeat drops

An actual defeat can leave one unopened Loot marker, one fixed Credits amount, or nothing at the former anchor Cell. The Unit's owned Equipment and Items return there with their physical identities independently of this roll. Undying and other defeat replacements bypass the drop. Credits merge with the existing pile and wait for an ordinary arrival. [Rules](CONSTRAINTS.md#drops-and-unopened-loot)

## Initial playtest values

The table records the Phase 2 baseline. The later content pass retains the same d6 rule and expands the set of Monster rewards. [Current Monster drop table](CREDITS_DROP_CONTENT_PASS.md#eligible-for-ordinary-expedition-population)

| Unit Type | Loot faces | Credits faces | Credits per pile | Nothing |
| --- | --- | --- | ---: | --- |
| Zombie | 1 | 2–3 | 3 | 4–6 |
| Skeleton Archer | 1 | 2 | 5 | 3–6 |

Both are ordinary Expedition population types. Each keeps its original one-in-six Loot chance. Their expected ground Credits per actual defeat are 1 and 5/6 respectively, before collection by either Side. Grunts still drop Loot only. Jackalopes still drop neither reward. Scenario authors can configure any Unit Type, including Heroes. [Roster](ProjectDelve.Engine/UnitRoster.cs) · [Authoring and overrides](LOOT_CONTENT.md#authoring-defeat-drops)

## RNG, observations and persistence

One d6 allocates Loot faces first, then Credits faces, then nothing. A Loot face yields nothing when the deck is empty or every remaining card is reserved. The Engine draws a card only at pickup. With Credits disabled, the original roll schedule and `LootDropRolled` event are preserved, including the roll for guaranteed Loot with supply. Six Credits faces place the fixed pile without a draw. This last case refines the investigation's proposal to roll whenever Credits are enabled. [Resolution](ProjectDelve.Engine/LootResolution.cs)

Credits-enabled Types emit `DefeatDropResolved` after `UnitDefeated`. The event carries the former Cell, the actual die face when rolled, and the marker or Credits amount awarded. Observers get the event only when the anchor Cell was explored at that step. Dropping Credits changes the board pile. The Side balance changes at collection. Playback and controllers use the same filtered state. [Engine visibility](ProjectDelve.Engine/FogOfWar.cs) · [Browser feedback](ProjectDelve.Web/BrowserProjection.cs)

The optional Unit Type fields retain zero defaults in old saves. An old scenario override that specifies only Loot odds retains its Loot-only behavior, even if the canonical Type now offers Credits. A new Loot override can explicitly inherit the Type's live Credits odds and amount. This distinction preserves the meaning of old files without copying either Credits value into new drafts. An active Expedition resumes with the stored Unit Types and RNG. Its archived region keeps unclaimed drops. The checkpoint format and version are unchanged. [Expedition regression](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs)

## Summons and limits

The Beastmaster's five companions, the Shaman's Goblin, and Workers from the Chief Mechanic or Geomancer have zero Credits faces. The Geomancer's Troll costs a Diamond and also has zero Credits faces. Shipped gear can summon a Goblin or Hobby Knight with limited uses, or a Budget Barbarian with a one-at-a-time limit. Those targets have zero Credits faces too. Custom content that pairs a repeatable summon with a Credits-paying Type can generate repeated piles when those Units are defeated. This follows the authored Type's odds. [Unit declarations](ProjectDelve.Engine/UnitRoster.cs) · [Shipped Equipment](ProjectDelve.Web/Content/Canonical/workspace-publication.bundle)

Very large custom amounts are subject to Phase 1's 32-bit headroom check. A pile that would exceed that limit rejects the resolving operation. The source game state remains intact. An external RNG supplied directly to the Engine can already have advanced on a mixed-outcome roll. Expedition commits its RNG together with the successful state boundary. [Pile validation](ProjectDelve.Engine/Credits.cs) · [Expedition boundary](ProjectDelve.Web/ExpeditionRunner.cs)

## Verification

The solution built successfully. The Engine suite, including generation, passed 3,838 tests. The Web suite passed 892. The client suite passed 1,145 and skipped its optional Beacon benchmark. NuGet vulnerability metadata was unavailable during the cached build.

- Scripted defeat rolls cover the face ranges, fully reserved decks, independent owned-object drops, Undying, merging, summons and Advance On Kill. A legacy explosion chain consumes faces 2 then 5 and leaves 4 as the next roll. Mixed rewards follow the same defeat order. [Engine regressions](ProjectDelve.Engine.Tests/CreditsDropTests.cs)
- One World Card defeats both a visible and a hidden Unit. Only the visible drop appears in the Side's observation and playback. A seeded Expedition replays the same Zombie drop after reopening, resumes the next decision identically and archives the unclaimed pile. [Visibility tests](ProjectDelve.Web.Tests/FogOfWarProjectionTests.cs) · [Expedition tests](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs)
- Designer and content tests cover effective values, invalid face sums and amounts, DELVE1, undo/redo, recovery and live inheritance. In Edge, a Designer-authored guaranteed drop produced a pile and the journal entry “5 Credits dropped at 3,1” while Amber's balance stayed at zero. A second walkthrough imported a legacy Loot-only override and enabled inherited Credits odds. [Designer API tests](ProjectDelve.Web.Tests/DesignerApiTests.cs) · [Designer client tests](ProjectDelve.Web.Tests/designer.test.cjs)
