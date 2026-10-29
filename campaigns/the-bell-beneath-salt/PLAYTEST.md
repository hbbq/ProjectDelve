# The Bell Beneath Salt — playtest record

This is an experimental campaign, with playable winning trajectories and known
balance risks. Automated success establishes neither enjoyment nor a reliable
human win rate. The campaign guide is [README.md](README.md).

## Method

Eight seeds (20261029–20261036), three Hero policies, five scenarios: 120 engine
runs, each capped at 25 rounds. [Full results](simulation-results.json) record
winners, rounds, casualties, remaining HP, living enemies and event counts.
Every scenario is loaded and validated through `ScenarioDefinitionJson` and
`GameEngine.CreateGame`. Dice, Activation Tokens, movement, actions, enemy
Behavior and completion all resolve through the existing engine.

- **Aggressive:** fight available enemies, then leave after clearing an extraction
  map. This tests the conservative combat-first strategy.
- **Objective:** prioritize designated targets and exit progress, but attack
  before moving when possible.
- **Rush:** advance toward an exit before attacking. On the non-extraction maps
  it deliberately behaves like Objective.

Heroes share simple healing, Rage/Focus/Dash choices, free door opening, and
friendly-fire avoidance. Heroes already inside an exit move toward distinct
standing cells to leave its entrance free. The policies do not coordinate a
full party plan, optimize every Ability, predict token draws, or systematically
test alternate routes. Objective and Rush also produced identical outcomes on
the extraction maps in this seed sample; these are not three independent
statistical estimates.

## Final results

Each entry is Blue wins out of eight seeds. Parentheses give the round range of
successful games. All outcomes use the final scenario definitions.

| Scenario | Aggressive | Objective | Rush |
| --- | --- | --- | --- |
| I — The Drowned Quay | 7/8 (4–6) | 5/8 (3–4) | 5/8 (3–4) |
| II — The Lantern Gallery | 8/8 (5–11) | 8/8 (5–11) | 8/8 (5–11) |
| III — The Choir Well | 4/8 (7–13) | 8/8 (3–6) | 8/8 (3–6) |
| IV — The Parting Tide | 6/8 (7–13) | 1/8 (6) | 1/8 (6) |
| V — The Bell Beneath Salt | 2/8 (13–17) | 5/8 (5–11) | 5/8 (5–11) |

There were no timeouts in the final 120 runs. The JSON includes the SHA-256 of
the tested library file so the observations can be tied to this content revision.

## What changed during playtesting

1. The lower Gallery buttress originally occupied a Cell behind the southern
   Door. It was moved west, making that approach usable.
2. Early Parting Tide runs punished immediate northward movement heavily. The
   rear Zombie was removed, leaving room to regroup across the southern edge
   before committing to the two exposed lanes.
3. The finale initially overwhelmed the simple policies with two immediate
   summoners plus two ranged guards. Both Shamans now start Lying, delaying their
   first summoning activation. The western Skeleton Archer was then removed,
   producing asymmetric flanks and a more open western approach.
4. The simulation's Hero attack policy was corrected to reject friendly
   Fireballs in both its explicit choices and fallback choices. Its extraction
   movement was also corrected: two Tide runs had eliminated every enemy with
   all Heroes healthy but stalled by occupying each other's exit approach.
   Distinct standing cells resolved those same two seeds into victories without
   any map or rules change. These bot faults were not used as reasons to weaken
   content.

Earlier runs informed these revisions; the result table and JSON contain only
the final content with the corrected policies. Changing a roster also changes
Activation Bag draws and subsequent random consumption, so a seed is not a
controlled comparison of identical dice rolls across revisions.

## Tactical findings and remaining weaknesses

- **I:** A quick crossing can succeed, but exposing the Rogue early can lose the
  mission in round two. Fighting first was safer in this sample and slower by
  roughly one or two rounds. There is no timer, so cautious clearance remains a
  strong strategy. This is a short introduction, not a route-solving puzzle.
- **II:** Doors and screened firing lanes create approach choices. All seeds
  were winnable by basic aggression, although several lost one or two Heroes.
  It may be forgiving for experienced players; its role is introducing ranged
  pressure and Door timing. The two approaches are geometrically available,
  but the automated policies do not prove that both are equally attractive.
- **III:** Focusing on the Shaman was substantially faster and more successful
  than clearing everything encountered. Once players recognize this, the
  target-selection lesson may become straightforward. This prepares the
  two-target finale rather than concealing the objective behind a trick.
- **IV:** The split party and all-survive condition create the sharpest failure
  risk. Direct escape performed poorly, while clearance followed by extraction
  usually worked. The safe southern regrouping space is a deliberate option.
  No timer forces players onward, so patient combat is likely the strongest
  general plan. Lethal early dice remain possible; human testing should focus
  on whether this feels demanding or punitive.
- **V:** The two summoners require target priority and choices about dividing
  the party. The open west flank contrasts with the surviving eastern ranged
  guard. The summoning loop can still snowball; a slow hunt can end in a party
  wipe. The greedy bots do not plan a coordinated pincer or reserve all limited
  Abilities well. This remains the least certain balance judgment and needs
  human playtesting.

No tested final victory occurred on the first activation or first round. The
maps have winning trajectories; this does not exhaustively rule out bad luck,
poor choices, dominant human strategies, or uninteresting repeated play.

## Edge inspection

The campaign selection flow and all five actual maps were inspected in headed
Microsoft Edge. Saved images are in [review/](review/), including
[campaign selection](review/campaign.png). The final finale view shows both
Shamans Lying. An actual Rogue activation moved from (7,11) to (8,8), attacked
and defeated the eastern Bomb Imp, took 1 Explosion damage, and advanced play
to the Wizard. This verifies a concrete tactical interaction in the browser;
it is not a full manual campaign playthrough. No console errors or failed
requests were observed during that inspection.

## Reproduce

```powershell
dotnet build tools/CampaignSimulation/CampaignSimulation.csproj --artifacts-path .playwright-cli
dotnet .playwright-cli/bin/CampaignSimulation/debug/CampaignSimulation.dll campaigns/the-bell-beneath-salt/library.json the-bell-beneath-salt 8 25 all
```

See the [simulation tool notes](../../tools/CampaignSimulation/README.md) for
policy limitations and optional single-scenario runs. Campaign layout and win
rates are authoring observations, not regression-test contracts.
