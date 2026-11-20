# Credits Drop Content Pass diagnostic

This sample compares the actual published canonical content before and after the content pass. The old effective content revision is the same revision measured in [Phase 7](../../ECONOMY_PHASE_7_ASSESSMENT.md). The original Phase 7 assessment and its raw measurements remain historical evidence.

The fixed seeds are **17, 42, 889, 2026, 77341, 1, 7, 123, 456, 1024, 8192, 65535**. Each population corpus generates indices 0–5 for all twelve seeds (72 regions). These are a deterministic convenience sample, not independent random observations or a balanced sample of every Monster Type. Gameplay starts at index 0 and enters up to two actual successors only after ordinary Party completion. The normal Expedition handoff preserves the Party's IDs, Levels, retained inventories and bank and restores all members to full effective HP, including members individually defeated in the completed map. No successor is fabricated after a Party defeat or cap.

The diagnostic uses the existing Phase 7 policy unchanged: attack first, use adjacent healing, scavenge until Round 8, try to liquidate through Round 12, then pursue the Exit; prioritize ground Credits and use ordinary enemy automation. Only authoritative legal candidates are submitted, with Fog of War enabled. The route heuristic knows board geography, Loot coordinates and ground Credits positions; this is not an unfamiliar human player's information policy. Stop at completion, no living Party members, or the existing 40-Round/3,200-decision cap. Ordinary `ExpeditionRandom` is initialized from the world seed and continued across played regions. The saved Expedition Runner's per-region gameplay seed schedule differs.

Population generation is paired by seed and region index. Gameplay is paired by world seed and policy, with the same initial random schedule. Giving a previously zero-Credits Type Credits faces can add a defeat roll and shift subsequent random consumption. Later combats, collections, survival and reachable successors may therefore diverge; the comparison does not hold every combat die fixed. Generated corpus expectations assume every initial Monster is defeated and are **potential ground money**, never earned income. Gameplay counts actual Engine defeat-drop and collection events separately from confirmed Station sales, board piles, retained sellable value and Party bank. No purchases, upgrades, recruitment or paid services are performed, so milestones describe a first affordable bank threshold only. Availability, eligibility and the effects of spending remain unmeasured.

## Reproduction

The baseline source revision is `d7871968ea624ab77eacab9ee1ce1bc71eeb021f`. Before content edits, the old Engine/Web dependencies were compiled into `.drop-assessment` and the old `workspace-publication.bundle` was copied into its `canonical-before` directory. The instrumented test was then compiled with `-p:BuildProjectReferences=false`, preserving those old dependencies. The new source was compiled independently into `.drop-after`, with its own pinned publication copy. Each artifact tree stayed exactly one directory below the repository root while executing, as required by Web content-root lookup. Copying the publication alone would not pin the C# definitions.

The production-content diff was checked: only `CreditsDropSuccesses`/`CreditsDropAmount` change in six C# Monster declarations, and only those two fields change on the published Demolition Imp. No Expedition setting, price, Party definition, enemy stat, Behavior, Loot face allocation or Engine rule changes between the paired runs. The all-Loot authored playtest's local opt-out is outside Expedition. To replay old content after this pass, use production source/publication from the baseline commit together with the current diagnostic harness; changing only the publication directory would leave new C# drops active.

Run the commands below in a fresh PowerShell session from the checkout to measure. Use the content-pass checkout for the new sample. For the old sample, create a separate checkout at `d787196` and copy the current `ProjectDelve.Web.Tests/ExpeditionEconomyAssessment.cs` into the corresponding test directory there before building. Each checkout supplies its own actual publication.

```powershell
dotnet restore ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --artifacts-path .drop-assessment --ignore-failed-sources
dotnet build ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --no-restore --artifacts-path .drop-assessment -c Release
$env:PROJECTDELVE_ECONOMY_SEEDS = '17,42,889,2026,77341,1,7,123,456,1024,8192,65535'
$env:PROJECTDELVE_ECONOMY_ASSESSMENT_OUTPUT = "$PWD/.drop-assessment/gameplay.json"
dotnet test ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --no-build --no-restore --artifacts-path .drop-assessment -c Release --filter FullyQualifiedName~ExpeditionEconomyAssessment --logger 'console;verbosity=detailed'
```

Compare each checkout's `gameplay.json` and `gameplay.json.frequency.json` with the corresponding before/after exports here. For the focused Boots replay, use the new checkout, set the seeds variable to `889`, choose a different output filename and filter to `FullyQualifiedName~ExpeditionEconomyAssessment.ExportDeterministicEconomySample`. Without the seeds override, the harness retains the original five-seed policy. Without an output variable, both diagnostics return immediately.

Run `python docs/credits-drop-content-pass/summarize.py` after both exports complete to regenerate `summary.json`, including full per-Type population counts, expected Credits, first affordability observations, legacy field comparison and Credits conservation diagnostics. The raw `.frequency.json` exports preserve every seed/index and Type allocation; the gameplay exports preserve every played region. Expectations and retained sellable value are deliberately separate from actual income.

## Generated population and potential

All 72 paired populations have identical per-Type counts before and after. They contain 5,869 initial Monsters. Credit-bearing Types account for 2,049 Monsters (34.9%) before and 4,411 (75.2%) after. The sampled regions contain no Red Dragon, so its new authored drop has no contribution here. Goblin and Troll remain zero-Credits targets; their repeatable summons have not acquired a Credits reward.

| Type | Count | Regions present / 72 | Before expected Credits | After expected Credits |
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
| **Total** | **5,869** | | **1,961.17** | **6,224.67** |

This raises hypothetical full-clear money by 3.174 times in the fixed generation corpus. Mean potential per region rises from 27.24 to 86.45. These totals include Monsters that the diagnostic never encounters or defeats, remote groups and later generated regions that the Party never reaches.

| Region index | Before mean potential | After mean potential |
| --- | ---: | ---: |
| 0 | 17.64 | 36.10 |
| 1 | 27.85 | 71.71 |
| 2 | 33.76 | 93.88 |
| 3 | 31.07 | 111.85 |
| 4 | 30.47 | 105.58 |
| 5 | 22.64 | 99.61 |

The changes therefore reach Types that this generator actually uses in early and later regions. This is a distribution and authored-reward finding; it does not establish that the Party can safely collect these amounts or how many regions a purchase takes.

The broadened baseline exactly reproduces **all fields of all nine** historical Phase 7 gameplay rows for the original five seeds. This checks the old binaries/publication pin and retention of the original policy; new population and milestone fields are additional diagnostics.

## Actual gameplay income and affordability

| Measurement | Before | After |
| --- | ---: | ---: |
| Played regions | 23 | 22 |
| Completed / Party defeated / capped | 12 / 9 / 2 | 11 / 7 / 4 |
| Credits created by actual defeat drops | 41 | 133 |
| Party collected | 35 | 120 |
| Other Sides collected | 6 | 9 |
| Credits left on sampled maps | 0 | 4 |
| Confirmed Station sale income | 6 | 9 |
| Actual Party income: collection + sales | 41 | 129 |
| Income per played region: min / median / max | 0 / 0 / 9 | 0 / 3.5 / 22 |
| Mean income per played region | 1.78 | 5.86 |
| Terminal bank per seed: median / max | 1.5 / 12 | 9 / 34 |

Every terminal retained inventory has zero additional sell potential. Credits conservation holds in every played region: actual drops equal Party collections plus other-Side collections plus remaining piles. Opening bank is not counted again as new income. The initial Monsters in played regions had hypothetical full-clear expectations of 564.17 before and 1,208.83 after; actual drops were only 41 and 133 respectively. These two full-clear totals describe different reached-region sets and cannot be treated as matched earned income.

| Seed | Before terminal bank | After terminal bank |
| --- | ---: | ---: |
| 17 | 0 | 0 |
| 42 | 3 | 3 |
| 889 | 3 | 20 |
| 2026 | 0 | 0 |
| 77341 | 0 | 0 |
| 1 | 9 | 16 |
| 7 | 12 | 15 |
| 123 | 0 | 22 |
| 456 | 3 | 19 |
| 1024 | 0 | 0 |
| 8192 | 11 | 34 |
| 65535 | 0 | 0 |

| Bank threshold / example price | Before: seeds reaching it | After: seeds reaching it |
| --- | ---: | ---: |
| 10 / Minor Treatment | 2 | 6 |
| 12 / Lucky Boots Level 2 | 1 | 6 |
| 15 / Last Resort Fireball | 0 | 6 |
| 24 / Barbarian Level 2 | 0 | 1 |
| 50 / Full Treatment | 0 | 0 |
| 75 / The Tycoon recruitment | 0 | 0 |

Before, seed 7 first reaches both 10 and 12 in index 2/Round 16; seed 8192 first reaches 10 in index 2/Round 5. After, seed 8192 first reaches 10 in index 0/Round 10, seed 123 first reaches both 12 and 15 in index 0/Round 27, and seed 8192 first reaches 24 in index 2/Round 8. These are separate banks and observational thresholds, not a shopping sequence: spending on one service would reduce what remains for another. `summary.json` records every seed's first region/Round for every threshold.

The broader old sample already demonstrates occasional small-cost affordability that the historical five-seed sample did not observe. The content pass makes low-cost purchasing power more frequent in this fixed policy sample, while five new-content seeds still end with zero income. Full Treatment and recruitment remain unobserved within the sampled horizon; no regions-to-recruit claim follows. Survival/completion counts do not establish a combat-balance effect, because drop-roll consumption and Credits-seeking routes can alter later play. A more skilled combat policy, spending policy or longer campaign could produce different results.

## Eligibility probe

The separately exported `after-889-eligibility.json` replays only seed 889 with the same choices and random schedule. Read-only observations inspect each resulting state while the Party bank is at least 12 Credits, record the first living Party inventory and first owned Lucky Boots, and request an ordinary Engine upgrade preview only if funded Boots are part of a naturally reached ready DelveMart allocation. The proposed upgrade is never submitted. An empty quote list therefore records no observed ready Boots upgrade quote; a funded bank alone is not an observed upgrade opportunity. This probe does not inject Equipment, relocate Units, create extra Actions, change the traversal policy or purchase anything.

To reproduce it, build the current diagnostic and use the command above with `PROJECTDELVE_ECONOMY_SEEDS='889'`, a separate output path, and filter `FullyQualifiedName~ExpeditionEconomyAssessment.ExportDeterministicEconomySample` to omit the population corpus.

The probe completed successfully and exactly matches every shared gameplay field of the broad seed-889 export. Region 0 completes with 8 Credits and four living Party members. After the normal full-Party handoff, region 1 first passes the 10- and 12-Credit thresholds in Round 1: the observed resulting state has 14 Credits and five living members. Barbarian owns a Level 1 Stonebreaker; nobody owns Lucky Boots. Across **664** resulting states with bank at least 12, no living member ever owns Lucky Boots, and no funded Boots Station upgrade quote is observed. The 15-Credit threshold is reached in Round 17. The sample stops at the Round-41 diagnostic cap with one living member and 20 Credits. This is observed purchasing power, including a funded living Party, but it provides no observed Lucky Boots upgrade opportunity or purchase. It also does not show that paid healing or Fireball was accessible: neither paid service Machine appears in these two regions.

## Verification and limits

Both old and new opt-in exports passed two tests each, covering 72 generated regions per content revision and 23/22 played regions. The focused eligibility replay passed one test, matched its broad replay exactly, and used the final diagnostic source. The old run completed in 11m53s, the new run in 12m24s and the targeted replay in 1m04s; these runs overlapped. All three isolated Release builds succeeded. Restore/build emitted `NU1900` because the vulnerability feed could not be reached; dependencies were available and all diagnostics completed. The summary script executed successfully, reporting zero legacy differences, zero paired population differences and zero Credits-conservation differences. Artifact trees were moved under ignored `.playwright-cli/credits-drop-before`, `credits-drop-after` and `credits-drop-probe` after execution; only source and diagnostic evidence are retained for review.

These diagnostics measure mutable published content; they are not regression assertions on generated layouts. They do not verify purchases or establish human pacing, optimal play, recruitment timing, service availability while funded, Red Dragon frequency, or general behavior of custom authored repeatable Credits sources. Existing focused mechanic and validation tests remain the behavioral verification boundary.
