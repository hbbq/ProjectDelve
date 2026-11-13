# Rock Enrichment / Mountain Dungeon phase probe

Run from the normal repository with .NET 10. No worktree, historical Session B
initializer, external package or alternate production algorithm is needed.
The harness calls public `WorldGenerator.Generate`, including ordinary Engine
creation/validation. Outputs are new JSONL files; existing outputs are refused.

```powershell
dotnet build tools/rock-enrichment-probe/RockEnrichmentProbe.csproj --artifacts-path .playwright-cli --ignore-failed-sources
$env:DOTNET_TieredCompilation='0'
dotnet .playwright-cli/bin/RockEnrichmentProbe/debug/RockEnrichmentProbe.dll .tmp/rock-rerun.jsonl
dotnet .playwright-cli/bin/RockEnrichmentProbe/debug/RockEnrichmentProbe.dll .tmp/rock-dungeons-rerun.jsonl --dungeons
dotnet .playwright-cli/bin/RockEnrichmentProbe/debug/RockEnrichmentProbe.dll tools/rock-enrichment-probe/evidence.jsonl --verify
dotnet .playwright-cli/bin/RockEnrichmentProbe/debug/RockEnrichmentProbe.dll .tmp/rock-maps.jsonl --maps
```

The terrain corpus is Session B's 45 paired worlds: 30x24, 60x50 and 120x120;
Woodland, Rocky Wilds, Dense Forest, Dense Rock and Fine Mixed; seeds 1 at (0,0),
42 at (-17,-13), and 123456789ABCDEF0 at (37,-29). Each runs disabled/Low/Medium/High
with only Rock enrichment. Each configuration has two warmups and five timed
samples, deterministic scenario/feature/entrance/opportunity checks, and per-stage
runtime/managed current-thread allocation measurements. Timing never controls
generation. The `--verify` mode performs one generation per compact evidence row.

The dungeon corpus contains all 144 Session B Rocky Wilds configurations plus
its 12 targeted opportunity configurations. It runs every recipe twice, checks
scenario and Site determinism, records explicit generation errors, and verifies
every placed dungeon's protected rock and usable room access to its retained
root. Omissions are distinct from failures. Dungeon timing is the first of two
runs with no warmups; these observations are not controlled latency benchmarks.

`Survey` reproduces the historical *intact-rock* geometric metric: cardinal
Solid Rock masses, eight-neighbor core, minimum buffered 3x3 rooms, greedy packing
and principal-region mouths. It is advisory, ignores construction ownership,
and is deliberately stricter than the absorption planner. Actual placements are
reported separately. Both before and after use the same survey.

The measured baseline is revision `22a1c4ebfa938ee28c5eac43a400222f19393b5f`,
which already contains Forest Phase 2 and Accessibility Analysis V1. The baseline
Generation assembly SHA256 is
`649DB39FAD9384F118D1109FAF31A1487F80BD9FB725E14B642432AD50DE19CA`.
It was built and archived before production edits, then measured using the same
harness. Local raw runs and archived binaries are in `.tmp/rock-phase3/`.
`after-intermediate.jsonl` is an earlier guard revision, not the delivered result.

Repository evidence:

- `results.json` and `evidence.jsonl`: 180 paired terrain outcomes, reproducible
  WLD1 codes, fingerprints, quality/work metrics and measured costs.
- `dungeon-results.json` and `dungeon-evidence.jsonl`: all 156 paired dungeon
  outcomes, including gains/losses, the two confirmed Fine Mixed losses, the
  non-guaranteed 120x120 case, positive Rocky Wilds control and six historical
  access failures.
- `rock-comparison.png`: original/baseline/current Rock geometry for the named
  120x120 Rocky Wilds, Dense Rock and Fine Mixed seed-42 examples.

With the archived local raw runs, regenerate summaries using Python 3:

```powershell
python tools/rock-enrichment-probe/compare.py .tmp/rock-phase3/before.jsonl .tmp/rock-phase3/after.jsonl --output tools/rock-enrichment-probe/results.json --evidence-output tools/rock-enrichment-probe/evidence.jsonl
python tools/rock-enrichment-probe/compare-dungeons.py .tmp/rock-phase3/dungeons-before.jsonl .tmp/rock-phase3/dungeons-after.jsonl --output tools/rock-enrichment-probe/dungeon-results.json --evidence-output tools/rock-enrichment-probe/dungeon-evidence.jsonl
python tools/rock-enrichment-probe/render-maps.py .tmp/rock-phase3/maps-before.jsonl .tmp/rock-phase3/maps-after.jsonl tools/rock-enrichment-probe/rock-comparison.png
```

Rendering uses Pillow; analysis scripts otherwise use Python's standard library.
Read [phase results](../../ProjectDelve.Generation/ROCK_ENRICHMENT_PHASE3_RESULTS.md)
for metric assumptions, tradeoffs, test results and remaining limitations.
