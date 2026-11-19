# Expedition introduction calibration

Across 100 matched first regions, `ExpeditionPopulation1` reduced mean hostile count by 51% and mean total threat by 66%. Introductory regions still contained 30–59 Monsters, and all eight ordinary Monster Types seen in the baseline also appeared in the introduction. All 100 openings had Loot markers, ranging from 3 to 54 with a mean of 16.88. The actual rewards depend on draws and exploration.

## Opening regions

Each pair used the default 60 × 60 Expedition recipe, the C# canonical roster and the same world seed (unsigned decimal 0–99). The ordinary variant used the pre-change generator. Base-terrain and geography hashes matched across all 100 pairs. Geography includes the Board, Sites, geographic routes and deployment. The 95th percentile uses nearest rank.

| Per-board measure | Ordinary mean | Intro mean | Ordinary 95th | Intro 95th |
|---|---:|---:|---:|---:|
| Hostiles | 90.46 | 44.29 | 167 | 54 |
| Total threat | 235.40 | 80.52 | 450 | 100 |
| Mean threat per hostile | 2.56 | 1.83 | 3.23 | 2.25 |
| Hostiles rated threat 4+ | 13.62 | 1.31 | 42 | 6 |
| Ranged hostiles | 11.09 | 1.48 | 27 | 6 |
| Most threat within 6 Cells of a hostile anchor | 29.88 | 16.22 | 44 | 23 |
| Largest single encounter by threat | 18.21 | 13.03 | 25 | 20 |
| Largest Alert component by Unit count | 6.48 | 5.69 | 11 | 8 |

Seed 36 had the highest ordinary total threat (530). Its introduction had 44 hostiles and 65 total threat. Seed 14 had 41 Skeleton Archers across its ordinary board and zero in its introduction.

## Arrival

For seeds 0–24, the diagnostic repeated the ordinary variant with an explicit legacy profile. All 25 complete Scenario hashes matched the pre-change binary. Distances below use the nearest pair of Cells across the Monster’s full footprint and the five original Hero positions.

| Per-formation measure, 25 matched origins | Ordinary mean | Intro mean |
|---|---:|---:|
| Distance to nearest hostile | 7.52 | 11.12 |
| Hostiles within 8 Cells | 4.32 | 0.68 |
| Threat within 8 Cells | 12.44 | 1.56 |
| Ranged hostiles within 8 Cells | 1.00 | 0.00 |
| Arrival points | 5.32 | 0.68 |

Seed 4 had 14 hostiles within eight Cells, including six Skeleton Archers, and the nearest hostile was one Cell away. The introduction placed two melee hostiles in that area, the nearest five Cells away. This was the highest exposure in the subset, falling from 20 to 2 points. The policy still permits a single ranged Monster nearby.

Seed 2 isolates the placement constraint. With an explicit Low/difficulty-1 recipe, both policies resolved to Low/1. Nearby hostiles fell from four to one while total population changed from 47 to 46.

## Progression and compatibility

Each row is a mean across seeds 0, 1 and 2, following each seed’s actual journey.

| Index | Ordinary hostiles | Intro hostiles | Ordinary total threat | Intro total threat |
|---|---:|---:|---:|---:|
| 0 | 111.0 | 46.3 | 304.7 | 86.3 |
| 1 | 115.7 | 92.7 | 300.7 | 197.7 |
| 2 | 92.7 | 92.7 | 265.7 | 222.3 |
| 3 | 85.0 | 85.0 | 233.7 | 221.0 |
| 4 | 90.7 | 90.7 | 238.3 | 238.3 |
| 5 | 84.7 | 84.7 | 236.0 | 236.0 |

All six complete Scenarios at indices 4 and 5 matched exactly, including Units, Orders and Loot. The same held for all eight later pairs in a separate seed-2 comparison of the default, Off/1, Low/1 and High/5 recipes. Off produced zero enemies at every tested index.

A second catalog check used the Web host’s shipped canonical definitions, including Demolition Imp. Across seeds 0–4, mean hostiles fell from 109.2 to 44.6 and mean total threat from 300.6 to 81.2. Demolition Imps appeared under both policies. Seed 0 at index 4 produced identical complete Scenarios. Across all samples and repeats, all 302 generations succeeded.

Threat uses the generator’s authoring ratings. The diagnostic reports Red Dragons separately and counts them in the 5+ band when present. Ranged means base Attack Range above 1. The distance measurements omit line of sight, movement, Alert propagation after setup and combat. Survival and Equipment acquisition rates require playtesting. Custom workspace catalogs can also change composition.

## Reproduction

From the repository root with dependencies restored, run the [calibration diagnostic](ProjectDelve.Web.Tests/ExpeditionIntroCalibration.cs).

```powershell
dotnet build ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --no-restore --artifacts-path .playwright-cli
$env:PROJECTDELVE_INTRO_OUTPUT = "$PWD/.tmp/intro-reproduction"
$env:PROJECTDELVE_INTRO_VARIANT = 'both'
$env:PROJECTDELVE_INTRO_CATALOG = 'csharp'
$env:PROJECTDELVE_INTRO_SEED_START = '0'
$env:PROJECTDELVE_INTRO_SEED_COUNT = '100'
$env:PROJECTDELVE_INTRO_INDICES = '0'
$env:PROJECTDELVE_INTRO_EXTREMES = '0'
dotnet test ProjectDelve.Web.Tests/ProjectDelve.Web.Tests.csproj --no-build --no-restore --artifacts-path .playwright-cli --filter FullyQualifiedName~ExpeditionIntroCalibration
```

For the journey comparison set the seed count to `3` and indices to `0,1,2,3,4,5`. For the extreme recipes set `PROJECTDELVE_INTRO_EXTREMES=1`, `PROJECTDELVE_INTRO_SEED_START=2`, count `1` and indices `0,1,4,5`. For the shipped catalog set `PROJECTDELVE_INTRO_CATALOG=published`. Use a fresh output directory when changing catalogs, content or implementation. Each run can resume completed JSON samples from its own directory.
