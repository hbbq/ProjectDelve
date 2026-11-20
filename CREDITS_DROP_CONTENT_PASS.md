# Credits drops in Expedition

Eight of the ten eligible Monster Types now have Credits faces. Six gain a new drop and the Skeleton Archer gains a second face. The prices of Lucky Boots Level 2, Barbarian Level 2 and The Tycoon remain 12, 24 and 75 Credits. All prices, Loot faces, combat stats, population weights and generation policies retain their Phase 7 values.

## Inventory and prices

The starting point was the committed Phase 7 catalog. The 39 C# Unit Types and two published additions below cover the shipped catalog. Expedition draws from a smaller pool of stable IDs. A Unit's Side in an authored game is independent of its theme. Stats are Level 1 **MOV/RNG/ATK/DEF/HP**. Threat is the generator's coarse composition band. The descriptions give the practical reasons two Units in the same band can play differently. [Unit roster](ProjectDelve.Engine/UnitRoster.cs) · [Published Types](ProjectDelve.Web/Content/Canonical/workspace-publication.bundle) · [Dungeon threat bands](ProjectDelve.Generation/DungeonGenerator.cs)

### Eligible for ordinary Expedition population

Loot is the original and retained number of d6 faces. Each Credits entry is **faces × pile amount**. The last column is expected ground Credits per actual defeat, before either Side collects anything. Zero means the Type has no Credits faces.

| Type and stable ID | Stats | Threat and encounter role | Summon target | Loot | Credits before → after | Expected before → after |
| --- | --- | --- | --- | ---: | --- | --- |
| Goblin · `goblin-type` | 4/1/2/2/1 | 1. Fast skirmisher, retreats after attacking. Goblin camps can contain groups. | Shaman, Goblin Whistle | 1 | 0 → 0 | 0 → 0 |
| Grunt · `grunt-type` | 3/1/3/3/1 | 2. Ordinary melee figure, also used for road patrols. | — | 1 | 0 → 3×3 | 0 → 1.50 |
| Zombie · `zombie-type` | 2/1/3/3/1 | 2. Slow melee, can break Doors. Common in houses and buildings. | — | 1 | 2×3 → 2×3 | 1 → 1 |
| Bomb Imp · `bomb-imp-type` | 3/1/2/2/1 | 3. Fragile at close range. Its defeat explosion can cause further defeats. | — | 1 | 0 → 2×4 | 0 → 1.33 |
| Demolition Imp · `demolition-imp` | 3/1/2/2/1 | 3. Half the Bomb Imp's general pool weight. Explodes and breaks adjacent objects on defeat. | — | 1 | 0 → 2×5 | 0 → 1.67 |
| Skeleton Archer · `skeleton-archer-type` | 3/4/3/3/1 | 3. Maintains firing distance. Reaching its pile can require a second advance. | — | 1 | 1×5 → 2×5 | 0.83 → 1.67 |
| Ghost · `ghost-type` | 2/1/3/3/1 | 3. Phase permits approaches across obstacles. Also a possible building defender. | — | 1 | 0 → 2×5 | 0 → 1.67 |
| Troll · `troll-type` | 2/1/4/4/1 | 4. Breaks Doors. Undying can replace an upright defeat with Lying, so defeating one may take another hit. | Geomancer, one Diamond | 2 | 0 → 0 | 0 → 0 |
| Displacer Demon · `displacer-demon-type` | 3/1/4/4/1 | 5. Swap, Displace and a friendly defence aura complicate a close fight. | — | 2 | 0 → 3×6 | 0 → 3 |
| Red Dragon · `red-dragon-type` | 2/4/4/4/8 | Rare Unique 2×2 defender. Difficulty 5, suitable large dungeon chamber, breath and claw attacks. | — | 3 | 0 → 2×12 | 0 → 4 |

Road patrols, Goblin camps and Zombie houses have fixed Type preferences. Buildings favour Zombies, with Ghosts and Trolls possible at higher difficulty. The general pool excludes Shaman. Large chambers can request one Red Dragon. Opening-region difficulty ramps from 1, with lower density and an arrival encounter allowance. These existing placement choices influence which new rewards a Party encounters. [Population composition](ProjectDelve.Generation/PopulationComposition.cs) · [Dungeon chambers](ProjectDelve.Generation/DungeonSitePopulation.cs) · [Opening regions](ProjectDelve.Generation/ExpeditionPopulationIntroduction.cs)

### Other combat, support and scenario Types

Every entry below is outside ordinary Expedition Monster population. Its Credits chance, fixed amount and expected Credits per defeat are zero before and after the pass. Paired chess figures have separate stable IDs and identical printed stats and Loot odds.

| Type and stable ID | Stats | Loot faces | Combat role, special purpose or summon path |
| --- | --- | ---: | --- |
| Shaman · `shaman-type` | 2/0/0/3/1 | 2 | No normal Attack. Flees and repeatedly summons Goblins. Standalone dungeon threat 4. |
| Geomancer · `geomancer-type` | 2/0/0/3/1 | 2 | No normal Attack. Resource specialist, summons Workers and can pay a Diamond to summon a Troll. |
| Worker · `worker-type` | 4/0/0/2/1 | 1 | Noncombat Resource transport and Operate. Repeatable Worker summon target. |
| Chief Mechanic · `chief-mechanic-type` | 3/0/0/4/3 | 2 | Unique nonattacking specialist, transports and gives Resources, operates Machines and summons Workers. |
| Diamond Kart · `diamond-kart-type` | 5/0/0/3/4 | 1 | Unique racing vehicle with resource-powered abilities. |
| Locomotive · `locomotive-type` | 0/0/0/0/1 | 1 | Rail movement, no normal Attack. |
| Labyrinth Shaper · `labyrinth-shaper-type` | 2/1/2/2/5 | 2 | Unique room-reshaping playtest figure. |
| Witch Queen · `witch-queen-type`, Royal Queen · `royal-queen-type` | 16/1/4/5/3 | 2 | Strong Unique corridor attackers, advance on kill. |
| Goblin King · `goblin-king-type`, Royal King · `royal-king-type` | 2/1/2/3/5 | 2 | Unique, durable adjacent movers with a friendly defence aura. |
| Goblin Bishop · `goblin-bishop-type`, Royal Bishop · `royal-bishop-type` | 16/1/3/4/1 | 1 | Diagonal corridors and advance on kill. |
| Hobby Knight · `hobby-knight-type`, Royal Knight · `royal-knight-type` | 3/1/3/4/1 | 1 | L-shaped movement. Knightly Boots summon the Hobby Knight. |
| Goblin Peasant · `goblin-peasant-type`, Royal Peasant · `royal-peasant-type` | 1/1/2/2/1 | 1 | Slow, weak melee figures that prefer low-defence targets. |
| Tower Troll · `tower-troll-type`, Castle Guard · `castle-guard-type` | 16/1/3/4/1 | 2 | Straight corridors and advance on kill. Tower Troll can also appear in the standalone stronghold. |
| Jackalope · `jackalope-type` | 5/0/0/2/1 | 0 | Fast nonattacking scout, operates Beacons. Beastmaster summon. |
| Giant Toad · `giant-toad-type` | 3/0/0/2/1 | 0 | Amphibious nonattacking scout. Beastmaster summon. |
| Dire Bear · `dire-bear-type` | 2/1/3/3/1 | 0 | Melee companion. Beastmaster summons it Lying. |
| Raptor · `raptor-type` | 4/1/2/2/1 | 0 | Mobile skirmisher. Beastmaster summons it Lying. |
| Light Elemental · `light-elemental-type` | 3/0/0/2/1 | 0 | Nonattacking defence-aura companion. Beastmaster summon. |
| Budget Barbarian · `budget-barbarian` | 3/1/4/3/2 | 1 | Unique temporary fighter from a Single Use Budget Barbarian Kit. |
| Barbarian · `barbarian-type` | 3/1/4/3/5 | 1 | Founding melee Hero with Cleave and Rage. |
| Rogue · `rogue-type` | 4/1/3/2/4 | 1 | Founding mobile Hero with Backstab. |
| Wizard · `wizard-type` | 3/4/3/2/4 | 1 | Founding ranged Hero with Fireball. |
| Cleric · `cleric-type` | 3/1/3/3/4 | 1 | Founding support Hero with healing and a defence aura. |
| Beastmaster · `beastmaster-type` | 3/1/2/3/5 | 1 | Founding Hero who can replace defeated companions. |
| The Tycoon · `tycoon-type` | 4/1/2/2/5 | 1 | Ordinary Unique recruitable Hero, 75 Credits. |

The shipped Scenario Library also has local definitions. The DelveMart Workshop pins its own Grunt (3/1/3/3/1, one Loot face, zero Credits). The three Practice Dummies, `furniture-practice-dummy`, `curiosity-practice-dummy` and `wave-practice-dummy`, and the Practice Target, `pocket-practice-target`, are 0/0/0/0/1. Threadkeeper (`pocket-threadkeeper`, 2/1/0/1/3) and Patient Friend (`pocket-patient`, 0/0/0/1/5) are local Ability demonstrations. Those six demonstration Types have zero reward faces and no summon source. Local definitions and explicit scenario overrides continue to take precedence. [Scenario Library](ProjectDelve.Web/Content/Library/library.json)

The built-in resource playtests have a Coal Collector (`coal-collector`, 2/0/0/3/1), Coal Courier (`coal-courier`, 3/0/0/1/1) and Press Operator (`operator`, 3/0/0/1/1). All three are nonattacking, unsummoned and have zero reward faces. A different local Coal Courier retains `zombie-type`, its Zombie stats and its existing 1 Loot face and 2×3 Credits drop. None of these local service roles enters Expedition population. The Loot and Equipment playtest separately reserves all six faces for Loot on its local Grunt and Bomb Imp. Their definitions now explicitly set the Credits fields to zero to preserve that demonstration. [Resource playtests](ProjectDelve.Web/PlaytestScenarios.cs) · [Loot playtest](ProjectDelve.Web/LootPlaytestScenario.cs)

## Summoning and calibration choices

Goblin is both an ordinary enemy and the Shaman's cost-free, unlimited summon. Keep it Loot-only. This retains the same physical Type for wild and summoned Goblins. The Goblin Whistle and Knightly Boots each have two summon uses. Expedition handoff can restore retained Equipment uses. Chief Mechanic and Geomancer can replace Workers up to their Side limit of three. The Geomancer's Troll consumes a Diamond. The Beastmaster can replace companions subject to its one-at-a-time limits. The Budget Barbarian Kit is consumed on use and has a one-per-Side living limit. None of these targets yields Credits. Loot from those that have Loot faces draws on the finite deck, and an Undying replacement creates no reward. [Summon declarations](ProjectDelve.Engine/UnitRoster.cs) · [Published Equipment and Item](ProjectDelve.Web/Content/Canonical/workspace-publication.bundle) · [Defeat rules](CONSTRAINTS.md#drops-and-unopened-loot)

The Grunt gives a common patrol enemy a frequent small pile. Increasing the Archer's chance keeps its familiar pile of 5. Bomb Imp, Demolition Imp and Ghost reward mid-tier fights, and the rarer Displacer Demon and Red Dragon offer larger piles. Even the Dragon retains three Loot faces and one empty face. The Zombie keeps its original reward and provides a comparison with Phase 7. A scenario author can still choose different odds or give a repeatable summon a Credits drop.

## RNG and existing games

A mixed drop uses one d6. With an unreserved Loot card available, a newly eligible Grunt consumes the same single roll as its old Loot-only definition, and face 1 still produces Loot. With an empty or fully reserved deck, the old Loot-only definition skipped the roll. The new mixed definition must roll because Credits are still possible. A different ground pile can also change later decisions and combat. Determinism applies when replaying the same captured definitions and choices. Previously materialized scenarios, active checkpoints and archived regions retain their stored odds. A new game or newly generated frontier uses its effective catalog. [Drop resolution](ProjectDelve.Engine/LootResolution.cs) · [Scenario overrides](LOOT_CONTENT.md#authoring-defeat-drops)

## Generated population and potential income

The paired generation sample used 12 world seeds and regions 0–5 of each journey. The old and new catalogs generated identical counts of every Type at every seed and index. The table counts real initial placements. Expected Credits are the sum of each Type's count × Credits faces × fixed amount ÷ 6, assuming an actual defeat of every figure. This calculation assigns no income to any Side.

| Type | Figures in 72 regions | Regions containing Type | Expected ground Credits before | Expected ground Credits after |
| --- | ---: | ---: | ---: | ---: |
| Zombie | 1,522 | 72 | 1,522 | 1,522 |
| Goblin | 1,132 | 69 | 0 | 0 |
| Grunt | 692 | 67 | 0 | 1,038 |
| Ghost | 630 | 60 | 0 | 1,050 |
| Bomb Imp | 595 | 64 | 0 | 793.33 |
| Skeleton Archer | 527 | 60 | 439.17 | 878.33 |
| Troll | 326 | 49 | 0 | 0 |
| Demolition Imp | 294 | 52 | 0 | 490 |
| Displacer Demon | 151 | 34 | 0 | 453 |
| Red Dragon | 0 | 0 | 0 | 0 |
| **All** | **5,869** | **72** | **1,961.17** | **6,224.67** |

Credit-bearing figures increased from 2,049 (34.9%) to 4,411 (75.2%). The full-clear expectation rose from 27.24 to 86.45 Credits per region. Red Dragon rewards were verified from content, with no Dragon encounter observed in this sample. [Before population](docs/credits-drop-content-pass/before.json.frequency.json) · [After population](docs/credits-drop-content-pass/after.json.frequency.json)

## Reproducing the gameplay comparison

Use world seeds `17, 42, 889, 2026, 77341, 1, 7, 123, 456, 1024, 8192, 65535`. The first five are the original Phase 7 seeds. The old catalog was pinned from commit `d787196`, including the C# definitions and published workspace bundle. The same policy and starting conditions were used with the new catalog. Each run starts with zero Credits, uses normal Monster automation, attempts liquidation at the arrival Station and enters up to three regions by ordinary completion. The policy spends nothing, so a price threshold records purchasing power at that moment. The fixed-seed gameplay runs are separate from the 72 generated populations above. [Commands, seeds, sampling limits and raw exports](docs/credits-drop-content-pass/ASSESSMENT.md) · [Simulation](ProjectDelve.Web.Tests/ExpeditionEconomyAssessment.cs)

The old catalog reproduced every recorded field in all nine original Phase 7 gameplay rows. That five-seed baseline produced 9 Credits, of which the Heroes collected 6 and other Sides collected 3. Sales yielded zero and the highest Party bank was 3. [Original Phase 7 results](ECONOMY_PHASE_7_ASSESSMENT.md#gameplay-income)

On those same five seeds with the new odds, Monsters produced 30 Credits. Heroes collected 23, the other Side collected 3, and 4 remained on the board. Confirmed sales again contributed zero. Seed 889 carried 8 Credits across the first Exit and reached 14 in Round 1 of the second region with five living Party members. A focused replay found that none of the living Party owned Lucky Boots at any of the 664 observed states with at least 12 Credits. It produced no funded Station quote for those Boots. The Party ended that sample with 20 Credits. [Targeted inventory and Station observations](docs/credits-drop-content-pass/after-889-eligibility.json)

## Actual income and affordability across twelve seeds

| Measured quantity | Phase 7 catalog | Expanded drops |
| --- | ---: | ---: |
| Regions actually played | 23 | 22 |
| Regions completed / Party defeated / diagnostic cap reached | 12 / 9 / 2 | 11 / 7 / 4 |
| Credits placed by defeats | 41 | 133 |
| Credits collected by Heroes | 35 | 120 |
| Credits collected by other Sides | 6 | 9 |
| Credits left on the board at region end or sample stop | 0 | 4 |
| Credits earned through confirmed sales | 6 | 9 |
| Mean Hero income from pickups and sales per played region | 1.78 | 5.86 |
| Highest Party balance | 12 | 34 |
| Runs reaching 10 / 12 / 15 / 24 / 50 / 75 Credits | 2 / 1 / 0 / 0 / 0 / 0 | 6 / 6 / 6 / 1 / 0 / 0 |

Every ground Credit is accounted for in both samples. A bank carried through an Exit appears again as the next opening bank and counts only once as income. On the *actually played* routes, defeating every initial Monster would have produced an expected 564.17 and 1,208.83 ground Credits respectively. The much smaller measured totals above include only resolved drops. Later turns and the number of regions entered can differ between the two runs of a seed. [Before gameplay](docs/credits-drop-content-pass/before.json) · [After gameplay](docs/credits-drop-content-pass/after.json) · [Aggregates and conservation checks](docs/credits-drop-content-pass/summary.json)

Closing balances below follow each real journey from its opening region (index 0). Each column ends at completion of the third sampled region, Party defeat, or the diagnostic cap. A terminal balance at defeat does not imply the Party could still spend it.

| Seed | Phase 7 balances | Expanded-drop balances |
| ---: | --- | --- |
| 17 | 0 → 0 | 0 → 0 |
| 42 | 3 | 3 |
| 889 | 0 → 3 | 8 → 20 |
| 2026 | 0 → 0 | 0 → 0 |
| 77341 | 0 → 0 | 0 → 0 |
| 1 | 0 → 9 → 9 | 0 → 16 |
| 7 | 3 → 3 → 12 | 4 → 9 → 15 |
| 123 | 0 | 22 |
| 456 | 3 → 3 | 3 → 19 |
| 1024 | 0 | 0 |
| 8192 | 3 → 8 → 11 | 11 → 21 → 34 |
| 65535 | 0 | 0 |

**Lucky Boots Level 2 is realistically fundable in early play.** Six of twelve runs reached 12 Credits, including seed 123 in the opening region at Round 27. With the former drops, only seed 7 reached that balance, in its third region at Round 16. A player still needs a physical pair of Boots and an eligible Station interaction. No upgrade purchase was simulated.

**Saving towards The Tycoon is observable, but 75 Credits remains unverified.** Seed 8192 finished three consecutive regions with 11, 21 and 34 Credits, ending with all five Party members alive and 41 Credits still needed. Six new-catalog runs never reached even 10 Credits. This sample gives no defensible recruitment region or guarantee that a surviving Party will eventually reach 75.

## Limits and next playtest

The policy uses a controlled roll sequence, basic attacks and available healing. Its movement heuristic knows the geography and Loot coordinates. It aims for the Exit after the early scavenging window, and stops after at most 40 rounds or 3,200 decisions per region. Human players can choose different targets, abilities, routes and sales. A Party saving every Credit also has more available for recruitment than one buying healing or upgrades.

Keep these values for human playtesting. Record the first round a Party both owns Lucky Boots and can upgrade them, how often Heroes must leave a pile to survive, and balances on successful fourth and later handoffs. Those observations can distinguish scarcity of Credits from difficulty obtaining the Equipment or returning to a Station.

## Verification

| Suite | Verified result |
| --- | --- |
| Engine and generation | 3,958 passed |
| Web and Expedition | 921 passed in a serial run. Earlier parallel runs encountered subscription and HTTP timeouts. |
| Browser client and QA dispatcher | 1,179 passed, with the optional Beacon performance benchmark skipped |
| Opt-in measurements | Both before and after exports passed, as did the separate seed 889 replay. The summary script reproduced the same aggregates. |

The build used cached dependencies. NuGet reported `NU1900` because its vulnerability feed was unavailable.

The published catalog round-trip checks each of the ten Expedition Types, its Loot faces, its Credits faces and its pile size. A second test follows the published Unit, Equipment and Item summon references, including additional Levels, and checks that their targets award zero Credits. Six scripted Grunt defeats compare every d6 face against its former definition with Loot available, including the next unused roll after DELVE1 transport. Expedition checkpoint tests replay the same Zombie, Grunt and Ghost defeats and recover the same uncollected piles from their archived boards. Combat, rail and hazard test fixtures that require an isolated dice sequence now explicitly disable incidental Credits drops. Their combat and event-order assertions still apply. [Catalog checks](ProjectDelve.Web.Tests/MonsterDropContentTests.cs) · [Engine dice checks](ProjectDelve.Engine.Tests/CreditsDropTests.cs) · [Checkpoint and replay tests](ProjectDelve.Web.Tests/ExpeditionPersistenceTests.cs)

The pass changes authored rewards and uses the existing collection, journal and board presentation. Client coverage used the automated suite. A new browser walkthrough would have repeated the Phase 2 presentation check. [Phase 2 browser verification](CREDITS_PHASE_2.md#verification)
