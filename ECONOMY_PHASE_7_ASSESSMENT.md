# Phase 7 Expedition economy assessment

The service placement policy is `ExpeditionEconomy1`. Each region independently requests one public DelveCare with probability 1/12 and one public Meteor-O-Mat with probability 1/12. Each has its own opportunity and placement seed domains. The generator tries at most 96 positions and omits an unavailable opportunity. It requires a clear 3 × 3 pad: the Machine and an eight-Cell operating/bypass ring. It makes no Terrain or Edge changes, avoids reservations and route corridors, and preserves initial figures, ground objects and unopened Loot. The completion guard and an additional initial-occupancy access check protect existing access, including Stations and outpost Doors.

The policy is recorded separately from nature, construction, Population and Stations. New runs record it immediately. Opening an older run keeps its materialized checkpoint and archived regions; only a successful new-region commit adopts the new policy. Generated paid Machine Instance IDs include the signed region origin, keeping vendor receipts and dispensed Item identities distinct across the journey even after an earlier Item is consumed. Authored Machine IDs are unchanged. A provider without the vending Item Type omits that optional Machine with a diagnostic. Existing authored Item definitions are used unchanged.

## Reproduction

The opt-in [assessment](ProjectDelve.Web.Tests/ExpeditionEconomyAssessment.cs) loads the Web's actual published canonical content, including `workspace-publication.bundle`. It does not inject Credits, Loot, Equipment or easier Monsters. The measured content revision is `1ba7ee5772045e64ac6b2670acfb1b7c7fd68e630e40e955f1a6d985921b4ef2`.

```powershell
dotnet restore ProjectDelve.sln --artifacts-path .phase7-economy --ignore-failed-sources
dotnet build ProjectDelve.sln --no-restore --artifacts-path .phase7-economy -c Release
$env:PROJECTDELVE_ECONOMY_ASSESSMENT_OUTPUT = "$PWD/.phase7-economy/final-assessment.json"
dotnet test ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --no-build --no-restore --artifacts-path .phase7-economy -c Release --filter FullyQualifiedName~ExpeditionEconomyAssessment --logger 'console;verbosity=detailed'
```

The output path holds gameplay measurements; the adjacent `.frequency.json` holds the placement corpus. The test returns immediately when the output variable is absent. Measurements are diagnostics, with no assertions about mutable authored content or generated layouts.

Gameplay uses seeds 17, 42, 889, 2026 and 77341, with up to three actual consecutive regions per seed. A run only enters a successor after ordinary Engine completion and carries its actual Party and bank. The policy attacks when possible, uses available ordinary healing, pursues Loot and discovered enemies until Round 8, attempts liquidation at the arrival Station through Round 12, then pursues the Exit. It prioritizes ground Credits. Enemy turns use ordinary automation. Every submitted decision comes from the Engine's current legal candidates with Fog of War enabled. The route heuristic knows the generated geography and Loot coordinates; this is a deterministic diagnostic policy, not a model of a new player's knowledge or optimal tactical play. Sampling stops on completion, defeat of all Party members, or a cap of 40 Rounds/3,200 decisions. Party defeat is a diagnostic stopping condition, not an added Engine completion rule.

The diagnostic uses the ordinary fair `ExpeditionRandom` implementation, initialized from each world seed and continued across its sampled regions. The saved-game Runner normally derives a fresh gameplay seed for each region. This controlled roll schedule is reproducible but does not claim to replay a saved campaign's dice.

Actual Credits created by defeat drops, Party collection, collection by other Sides and confirmed Station sales are counted separately from Engine events. Merely carrying a sellable object is potential income. Expected initial drop Credits assume every initial Monster is actually defeated and use its authored probabilities; they are not earned income. The policy purchases no upgrades, recruits or services, so bank thresholds measure affordability only.

## Placement frequency

The default-profile corpus contains 30 full regions: all five seeds above at region indices 0–5. All generated and passed final validation.

| Machine | Requests | Placed | Omitted | Actual regional frequency |
| --- | ---: | ---: | ---: | ---: |
| DelveCare | 2 | 1 | 1 | 1/30 (3.3%) |
| Meteor-O-Mat | 3 | 3 | 0 | 3/30 (10.0%) |

The accepted DelveCare is seed 42, index 4. Meteor-O-Mat appears at seed 17/index 0 and seed 77341/indices 3 and 5. Seed 42/index 1 omits DelveCare because no legal pad was found in the bounded search. Independent local-seed checks over 0–1199 request DelveCare 87 times (7.25%) and Meteor-O-Mat 93 times (7.75%). These finite samples do not replace the configured 1/12 probability.

A preliminary 5 × 5 pad placed only one of the same five requests. The final 3 × 3 ring increases actual availability while retaining the occupied-access and unchanged-content guarantees. Arrival DelveMarts and their separate additional-Station policy are unchanged.

## Gameplay income

The five seeds produced nine actually played regions. Four starting regions completed; the other five samples ended with no living Party members. None reached the diagnostic cap. No hypothetical crossing or restored Party was inserted after a defeat.

| Seed | Region index | Outcome | Round | Credits created | Party collected | Other Sides collected | Confirmed sales | Closing Party bank |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 17 | 0 | Completed | 35 | 0 | 0 | 0 | 0 | 0 |
| 17 | 1 | Party defeated | 26 | 0 | 0 | 0 | 0 | 0 |
| 42 | 0 | Party defeated | 37 | 3 | 3 | 0 | 0 | 3 |
| 889 | 0 | Completed | 30 | 0 | 0 | 0 | 0 | 0 |
| 889 | 1 | Party defeated | 33 | 3 | 3 | 0 | 0 | 3 |
| 2026 | 0 | Completed | 32 | 0 | 0 | 0 | 0 | 0 |
| 2026 | 1 | Party defeated | 26 | 0 | 0 | 0 | 0 | 0 |
| 77341 | 0 | Completed | 22 | 0 | 0 | 0 | 0 | 0 |
| 77341 | 1 | Party defeated | 19 | 3 | 0 | 3 | 0 | 0 |

Totals: 9 Credits created, 6 collected by the Party and 3 by another Side. Confirmed sales contributed 0. Every closing inventory had 0 additional sellable value, so hypothetical liquidation does not increase these closing balances. No uncollected pile remained at the sampled boundaries. Closing bank and earned Party income per region both had minimum 0, median 0 and maximum 3; mean earned income was 0.67 Credits per played region. Terminal bank per seed was 0, 3, 3, 0 and 0, with median 0.

There was no observed first affordable Minor Treatment, priced Equipment upgrade, Fireball purchase, Unit upgrade, Full Treatment or recruitment: none of the sampled banks reached even 10 Credits. No purchase milestone or number of regions to recruit can therefore be inferred. The four completed first regions all exited with 0 Credits. Region continuation itself creates no income.

The nine initial populations had hypothetical expected drop totals ranging from 9 to 27.5 Credits per region, 187.5 in aggregate, **if every initial Monster were defeated**. Actual generated money was 9. Treating the theoretical 187.5 as income would overstate affordability and ignore combat, collection and competing Sides. This simple policy also lost all Party members in five samples and uses only basic combat/healing; it cannot establish the pacing of skilled combat-focused or longer scavenging play. These measurements show that the current prices are not reached automatically by early traversal. They do not justify a price change by themselves.

## Prices and farming

Existing prices remain unchanged: Minor Treatment 10 Credits, Full Treatment 50, a Last Resort Fireball 15, Lucky Boots Level 2 at 12, Barbarian Level 2 at 24, and The Tycoon recruitment at 75. Reaching a bank threshold does not establish that a suitable Machine has been discovered, its target is eligible, or a purchase has occurred.

The published Last Resort Fireball has no Sell Value, so the current vending purchase cannot be resold for Credits. Healing creates no salable object. Unit and Equipment upgrades do not increase their Type's Sell Value. The existing repeatable summon targets have zero Credits faces; Loot-producing targets remain subject to the finite deck. This assessment does not introduce a summon exception or a hidden farming cap. Custom content that gives a repeatable summon target Credits drops, or gives an unlimited vending Item a Sell Value above its purchase price, can create repeatable income and requires separate authoring review. See [existing drop values and summon analysis](CREDITS_PHASE_2.md) and [sale values](CREDITS_PHASE_3.md).

For scale only, defeating a Zombie produces an expected 1 ground Credit and defeating a Skeleton Archer produces 5/6, before either Side collects anything. Under the hypothetical assumptions of unlimited targets and perfect collection, the 10/12/15/24/50/75-Credit thresholds correspond to 10/12/15/24/50/75 Zombie defeats or 12/15/18/29/60/90 Skeleton Archer defeats. These are arithmetic comparisons, not observed purchase milestones or region counts.

## Verification

Focused generation coverage checks independent request streams, deterministic placement, exact preservation of prior geography/Population/Loot and deployment, public ownership, occupied access, DELVE1 round trips, unsupported policy versions, legacy WLD1 behavior, missing Item omission and omission without Terrain repair. Persistence coverage includes an old Station-present profile without the economy policy, failed handoffs, unchanged archive bytes and successful frontier adoption.

The solution built successfully in an isolated Release artifact tree. The final focused generation suite passed 8 cases. The wider generation/Expedition Engine selection passed 317 cases before the final identity assertion and diagnostic-only refinements; the Expedition-only selection passed 50. Relevant Web persistence, Station, population-compatibility and journey cases passed 66. Both opt-in assessment exports passed, covering 30 generated regions and nine actually played regions. The final 3 × 3 placement/money sample was repeated with region-qualified physical IDs; the economic totals and placement counts matched. `git diff --check` passed for this scope. Restore/build reported `NU1900` because the NuGet vulnerability feed was unavailable; dependencies were available and compilation/tests completed.
